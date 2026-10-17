"""Keep the shipped offline legal text identical to the reviewable documents."""
import html
from pathlib import Path
import re

ROOT=Path(__file__).resolve().parent.parent


def html_links(text):
    escaped=html.escape(text)
    return re.sub(r'\[([^\]\n]+)\]\((https://[^\s)]+)\)',r'<a href="\2">\1</a>',escaped)


def main():
    output=ROOT/'Assets/VRPlayer/Resources/Legal'
    output.mkdir(parents=True,exist_ok=True)
    streaming=ROOT/'Assets/StreamingAssets/Legal'
    streaming.mkdir(parents=True,exist_ok=True)
    web=ROOT/'Builds/Compliance'
    web.mkdir(parents=True,exist_ok=True)
    for topic, source in [('privacy','PrivacyPolicy'),('permissions','Permissions')]:
        for language in ('zh-CN','en'):
            text=(ROOT/'Docs'/f'{source}.{language}.md').read_text(encoding='utf-8')
            plain=re.sub(r'^#+\s+', '', text, flags=re.M)
            (output/f'{topic}-{language}.txt').write_text(plain,encoding='utf-8',newline='\n')
            (streaming/f'{topic}-{language}.txt').write_text(plain,encoding='utf-8',newline='\n')
            (web/f'{topic}-{language}.html').write_text(
                '<!doctype html><html lang="'+language+'"><meta charset="utf-8">'
                '<meta name="viewport" content="width=device-width,initial-scale=1">'
                '<title>L\'s VRPlayer '+topic+'</title><style>body{max-width:54em;margin:3em auto;padding:0 1em;line-height:1.7;font:18px/1.7 system-ui}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:inherit}</style>'
                '<body><pre>'+html_links(plain)+'</pre></body></html>',encoding='utf-8')
    print('Offline legal documents and standalone HTML updated.')


if __name__=='__main__':
    main()
