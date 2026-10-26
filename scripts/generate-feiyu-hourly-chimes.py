"""Build offline Mosaic hourly clips using the verified Feiyu cute model package.

The CUDA inference environment is a build tool; Mosaic only needs the resulting WAVs.
Run with --hours 12 for a smoke sample, or omit --hours to build all 24 clips.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time


def sha(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda: f.read(8 * 1024 * 1024), b''): h.update(block)
    return h.hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--runtime-repo', type=Path, required=True)
    parser.add_argument('--service-source', type=Path, required=True)
    parser.add_argument('--model-package', type=Path, required=True)
    parser.add_argument('--catalog', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--hours', type=int, nargs='+')
    args = parser.parse_args()
    repo, package, output = args.runtime_repo.resolve(), args.model_package.resolve(), args.output.resolve()
    if output == package or output.is_relative_to(package) or output == repo or output.is_relative_to(repo):
        raise ValueError('Output must be outside the immutable model package and inference source')
    output.mkdir(parents=True, exist_ok=True)
    os.environ.update(HF_HUB_OFFLINE='1', TRANSFORMERS_OFFLINE='1', PYTHONDONTWRITEBYTECODE='1', OMP_NUM_THREADS='4', PYTHONUTF8='1')
    os.environ.setdefault('HF_HOME', str(output.parent / 'hf-cache'))
    os.environ.setdefault('MPLCONFIGDIR', str(output.parent / 'mpl-cache'))
    sys.path[:0] = [str(args.service_source.resolve()), str(repo), str(repo / 'third_party/Matcha-TTS')]
    from feiyu_runtime.common import verify_package, cpu_onnx_sessions
    manifest = verify_package(package)
    commit = subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip()
    expected_commit = manifest['runtime']['commit']
    if commit != expected_commit: raise ValueError('CosyVoice source revision differs from Feiyu package')
    matcha = subprocess.check_output(['git', '-C', str(repo / 'third_party/Matcha-TTS'), 'rev-parse', 'HEAD'], text=True).strip()
    pinned = subprocess.check_output(['git', '-C', str(repo), 'ls-tree', 'HEAD', 'third_party/Matcha-TTS'], text=True).split()[2]
    if matcha != pinned: raise ValueError('Matcha source revision mismatch')
    selected = manifest['styles']['cute']
    checkpoint, reference = package / selected['llm_checkpoint'], package / selected['reference_audio']
    if sha(checkpoint) != selected['llm_sha256'] or sha(reference) != selected['reference_sha256']:
        raise ValueError('Feiyu cute checkpoint/reference integrity mismatch')
    catalog = json.loads(args.catalog.read_text(encoding='utf-8-sig'))
    if sorted(row['hour'] for row in catalog) != list(range(24)): raise ValueError('Catalog must cover hours 0..23')
    hours = set(args.hours or range(24))
    import numpy as np
    import soundfile as sf
    import torch
    import torchaudio
    import onnxruntime as ort
    from scipy.signal import resample_poly
    if str(torch.__version__) != '2.11.0+cu128' or str(torchaudio.__version__) != '2.11.0+cu128':
        raise RuntimeError('Use the Feiyu pinned CUDA torch/torchaudio versions')
    if not torch.cuda.is_available(): raise RuntimeError('CUDA unavailable; refusing an unverified CPU substitution')
    torch.set_num_threads(4)
    # The upstream WAV loader explicitly selects SoundFile. Use the same PCM decoder
    # with torchaudio 2.11 on Windows, where the legacy backend keyword was removed.
    def read_pcm(path, frame_offset=0, num_frames=-1, normalize=True, channels_first=True, **kwargs):
        data, rate = sf.read(path, start=frame_offset, frames=num_frames, dtype='float32', always_2d=True)
        return torch.from_numpy(data.T.copy() if channels_first else data.copy()), rate
    torchaudio.load = read_pcm
    from cosyvoice.cli.cosyvoice import AutoModel
    from cosyvoice.utils.common import set_all_random_seed
    started = time.perf_counter()
    sessions = []
    with cpu_onnx_sessions(ort, sessions):
        voice = AutoModel(model_dir=str(package / manifest['base_model_dir']), fp16=False, load_trt=False, load_vllm=False)
    state = torch.load(checkpoint, map_location='cpu', weights_only=True)
    voice.model.llm.load_state_dict(state, strict=True)
    del state
    voice.model.llm.eval()
    for network in ('llm', 'flow', 'hift'):
        if next(getattr(voice.model, network).parameters()).device.type != 'cuda': raise RuntimeError('Network not on CUDA: ' + network)
    params = manifest['inference']
    prompt = params['prompt_prefix'] + selected['reference_text']
    voice.add_zero_shot_spk(prompt, str(reference), 'mosaic-feiyu-cute')
    print('MODEL READY: Feiyu cute F6 on ' + torch.cuda.get_device_name(0), flush=True)
    result = []
    for row in catalog:
        hour = row['hour']
        if hour not in hours: continue
        set_all_random_seed(params['seed'])
        start = time.perf_counter()
        with torch.inference_mode():
            chunks = [x['tts_speech'].detach().cpu().numpy()[0] for x in voice.inference_zero_shot(
                row['text'], prompt, str(reference), zero_shot_spk_id='mosaic-feiyu-cute', stream=False,
                speed=params['speed'], text_frontend=True)]
        if not chunks: raise ValueError('Empty TTS output')
        audio = np.concatenate(chunks)
        seconds = len(audio) / voice.sample_rate
        if not 2 < seconds < 25 or not np.isfinite(audio).all() or abs(audio).max() <= .001:
            raise ValueError(f'Invalid audio at hour {hour}: {seconds}s')
        # Preserve the existing Mosaic PCM playback contract without changing pitch/speed.
        playback = resample_poly(audio, 147, 160) if voice.sample_rate == 24000 else audio
        if voice.sample_rate != 24000: raise ValueError('Unexpected Feiyu source sample rate')
        gain = min(1., .98 / float(abs(playback).max()))
        name = f'hour-{hour:02d}.wav'
        sf.write(output / name, playback * gain, 22050, subtype='PCM_16')
        entry = dict(hour=hour, file=name, text=row['text'], voice='Feiyu / CosyVoice3 cute F6', style='cute',
                     source='https://github.com/Esmerubphire/astrbot_plugin_feiyu_voice',
                     model='Esmerubphire/FeiyuVoice-CosyVoice3', checkpoint_sha256=selected['llm_sha256'],
                     reference_sha256=selected['reference_sha256'], seconds=seconds, sample_rate=22050,
                     source_sample_rate=24000, sha256=sha(output / name), inference_seed=params['seed'])
        result.append(entry)
        print(f'OK hour {hour:02d}: {seconds:.2f}s audio, {time.perf_counter()-start:.1f}s inference', flush=True)
    (output / 'lines.json').write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    (output / 'generation.json').write_text(json.dumps(dict(passed=True, clips=len(result), style='cute',
        torch=str(torch.__version__), gpu=torch.cuda.get_device_name(0), precision='float32',
        cosyvoice_commit=commit, matcha_commit=matcha, model_manifest_sha256=sha(package / 'manifest.json'),
        onnx_sessions=sessions, text_frontend=voice.frontend.text_frontend,
        wav_decoder='SoundFile (torchaudio load adapter for Windows)',
        elapsed_seconds=time.perf_counter()-started), ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print('FEIYU GENERATION COMPLETE', flush=True)


if __name__ == '__main__': main()
