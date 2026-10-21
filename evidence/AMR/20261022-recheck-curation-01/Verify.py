# -*- coding: utf-8 -*-
"""Validate archived evidence; this does not build or execute the product."""
from pathlib import Path
import json, hashlib, subprocess, re, xml.etree.ElementTree as ET

E=Path(__file__).resolve().parent
R=E.parents[2]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def git(*args,data=None):
 p=subprocess.run(['git',*args],cwd=R,input=data,capture_output=True)
 if p.returncode:raise RuntimeError(p.stderr.decode('utf-8',errors='replace'))
 return p.stdout
j=json.loads((E/'curation.json').read_text(encoding='utf-8'))
for f in j['retainedEvidence']:assert sha(R/f['path'])==f['sha256'],f['path']
excluded={f['path'] for f in j['excluded']}
assert all(not (R/p).exists() for p in excluded)
old=R/'evidence/AMR/20261021-movement-recheck-01'
art=json.loads((old/'artifact-sha256.json').read_text(encoding='utf-8-sig'))
matched=skipped=0
for f in art:
 p=f['Path'].replace('\\','/')
 if p in excluded:skipped+=1;continue
 assert sha(R/p).upper()==f['SHA256'],p
 matched+=1
assert matched==69 and skipped==1
source=json.loads((old/'elevated-source-sha256.json').read_text(encoding='utf-8-sig'))
raw=git('cat-file','--batch',data=('\n'.join(j['priorSourceCommit']+':'+s['Path'].replace('\\','/') for s in source)+'\n').encode())
offset=0
for s in source:
 end=raw.index(b'\n',offset);header=raw[offset:end].decode();offset=end+1
 assert not header.endswith(' missing'),s['Path']
 size=int(header.split()[-1]);blob=raw[offset:offset+size];offset+=size+1
 variants=[blob,blob.replace(b'\r\n',b'\n').replace(b'\n',b'\r\n')]
 assert s['SHA256'] in [hashlib.sha256(b).hexdigest().upper() for b in variants],s['Path']
c=next(e.attrib for e in ET.parse(old/'elevated-tests.trx').iter() if e.tag.endswith('Counters'))
assert c['total']==c['passed']=='151' and c['failed']=='0'
v=json.loads((R/'evidence/AMR/20261021-movement-recheck-console-01/verification.json').read_text(encoding='utf-8-sig'))
assert v['Passed'] and len(v['Cases'])==15 and all(x['Passed'] and x['ExpectedExitCode']==x['ActualExitCode'] for x in v['Cases'])
assert not git('diff',j['base'],'--name-only','--','src','tests','config','global.json','AmrControlSystem.slnx')
assert not git('diff','--cached','--name-only','--','src','tests','config','global.json','AmrControlSystem.slnx')
for f in j['retainedEvidence']:
 assert not re.search(rb'(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)',(R/f['path']).read_bytes())
links=0
for doc in [E/'README.md',R/'README.md']:
 text=doc.read_text(encoding='utf-8-sig')
 # Inspect the new curation/review references; older README sections retain historical links.
 for target in re.findall(r'\]\(([^)]+)\)',text):
  if doc.name=='README.md' and doc.parent==R and '20261022-' not in target:continue
  target=target.strip('<>')
  if '://' in target or target.startswith('#'):continue
  assert (doc.parent/target.split('#')[0]).resolve().exists(),target
  links+=1
print(json.dumps({'passed':True,'kind':'evidence/document validation only','head':git('rev-parse','HEAD').decode().strip(),
 'retainedRawFiles':70,'originalManifestMatches':matched,'expectedExcludedManifestEntries':skipped,
 'priorSourceMatches':len(source),'priorUnitPassed':151,'priorConsolePassed':15,
 'localLinksChecked':links,'productSourceDiff':0,'productTestsExecuted':0,'secretPatternFindings':0},indent=2))
