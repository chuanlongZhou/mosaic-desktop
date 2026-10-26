"""Import original pi-deepseek-pet WebM assets without resizing or re-encoding."""
import argparse, hashlib, json, pathlib, re, shutil, subprocess

def main():
    p=argparse.ArgumentParser();p.add_argument('--source',type=pathlib.Path,required=True);p.add_argument('--output',type=pathlib.Path,required=True);a=p.parse_args()
    raw=(a.source/'assets/animation-manifest.jsonc').read_text(encoding='utf-8-sig')
    m=json.loads(re.sub(r',(\s*[}\]])',r'\1',re.sub(r'(?m)^\s*//.*$','',raw)))
    a.output.mkdir(parents=True,exist_ok=True); clips=[]
    for index,name in enumerate(m['assets']):
        src=a.source/'assets/animations/webm'/f'{name}.webm'; file=f'a{index:03d}.webm';shutil.copy2(src,a.output/file)
        clips.append(dict(name=name,file=file,sha256=hashlib.sha256(src.read_bytes()).hexdigest()))
    m['clips']=clips;m['source']='https://github.com/SchneeHertz/pi-deepseek-pet';m['sourceCommit']=subprocess.check_output(['git','-C',str(a.source),'rev-parse','HEAD'],text=True).strip()
    (a.output/'animations.json').write_text(json.dumps(m,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'Imported {len(clips)} original alpha WebM clips.',flush=True)
if __name__=='__main__':main()
