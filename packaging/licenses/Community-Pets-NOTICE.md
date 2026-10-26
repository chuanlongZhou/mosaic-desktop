# Community pet artwork and attribution

Mosaic includes optional, unofficial fan-art pet appearances from two public
repositories. The original Mosaic pet artwork remains available. Mosaic is not
affiliated with or endorsed by OpenAI, Anthropic, Google, xAI, DeepSeek,
Moonshot AI, Alibaba, or Zhipu AI. Their names and marks belong to their
respective owners and identify the inspirations only.

## chatgpt-chibi-pets

- Creator and source: **fywdy**, <https://github.com/fywdy/chatgpt-chibi-pets>
- Original notice: <https://github.com/fywdy/chatgpt-chibi-pets/blob/main/NOTICE.md>
- Art license: **Creative Commons Attribution-NonCommercial 4.0 International
  (CC BY-NC 4.0)**, <https://creativecommons.org/licenses/by-nc/4.0/>
- Mosaic uses the `dist/codex-native` v2 WebP spritesheets for Grok, Kimi,
  and Zhipu. Selected cells were cropped and repacked into transparent PNG
  atlases. Pose mapping and source SHA-256 values are in
  `src/Mosaic.Host/Assets/Pet/{GrokCommunity,KimiCommunity,ZhipuCommunity}/provenance.json` in
  the source repository. The original 11 Mosaic appearances were not replaced.

## CrossPet

- Project and source: **CrossPet** by **lokicorvus** and contributors,
  <https://github.com/lokicorvus/crosspet>
- Original character license and detailed credits:
  <https://github.com/lokicorvus/crosspet/blob/main/characters/LICENSE.md>
- Art license: **Creative Commons Attribution-NonCommercial-ShareAlike 4.0
  International (CC BY-NC-SA 4.0)**,
  <https://creativecommons.org/licenses/by-nc-sa/4.0/>
- Mosaic uses selected `characters/{gpt,claude,deepseek,gemini,qwen,glm}`
  WebP poses. They were resized and repacked into transparent PNG atlases.
  These adapted CrossPet atlases are shared under **CC BY-NC-SA 4.0**. Source
  filenames, SHA-256 values, and changes are recorded in each
  `src/Mosaic.Host/Assets/Pet/CrossPet*/provenance.json` in the source repository.

CrossPet credits the following original character concepts: **ZipZipPipe**
(GPT white dragon, DeepSeek whale maid, GLM black fox maid, Qwen Chinese-style
lady), **上善无形** (DeepSeek “溟月”), and community Gemini and Claude girl
designs. CrossPet created its own GLM and Qwen personality/action details.
See its `characters/LICENSE.md` for the complete original attribution and
background.

These imported artworks may be used **only for noncommercial purposes** and
must retain the attribution and applicable license. CrossPet derivatives must
also be shared under CC BY-NC-SA 4.0. No code from either repository was
copied into Mosaic; the notices above cover the imported art.
