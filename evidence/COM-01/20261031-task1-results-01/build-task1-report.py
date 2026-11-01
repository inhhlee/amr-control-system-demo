import hashlib
import html
import json
import re
from pathlib import Path

ROOT = Path('C:/amr-control-system-demo/evidence/COM-01/20261031-task1-results-01')
REPO = Path('C:/amr-control-system-demo')

def read(name):
    return (ROOT / name).read_text(encoding='utf-8-sig').strip()

def selected(text, pattern):
    return '\n'.join(line for line in text.splitlines() if re.search(pattern, line))

verification = json.loads(read('verification.json'))
assert verification['passed'] and len(verification['checks']) == 17
assert all(c['passed'] for c in verification['checks'])
receiver = read('receiver.stdout.log')
monitor = read('monitor.stdout.log')
fresh = read('fresh.stdout.log')
received = [json.loads(line.split(' Received ', 1)[1]) for line in receiver.splitlines() if ' Received ' in line]
assert len(received) == 7
initial = received[:6]
assert [m['Kind'] for m in initial] == ['heartbeat', 'heartbeat', 'telemetry', 'telemetry', 'alarm', 'ack']
assert [(m['AmrId'], m['Model']['Position'], m['Model']['Speed'], m['Model']['State']) for m in initial if m['Kind'] == 'telemetry'] == [
    ('DEMO_AMR_001', {'X': 1.25, 'Y': 2.5}, 0.5, 'BUSY'),
    ('DEMO_AMR_002', {'X': 3, 'Y': 4}, 0, 'IDLE'),
]
assert initial[-1]['Model']['CommandId'] == 'DEMO_RESPONSE_001'
assert initial[-1]['Model']['Result'] == 'SUCCESS'
assert initial[-1]['Model']['Id'] is None
assert received[-1]['Model']['Timestamp'] == 200
assert len(re.findall(r' Rejected ', receiver)) == 1
assert 'Rejected Code=MissingField Field=$.ts' in receiver
events = {}
for name in ['HeartbeatMonitoringStarted', 'HeartbeatTimedOut', 'HeartbeatRecovered']:
    lines = [line for line in monitor.splitlines() if f' {name} ' in line]
    assert len(lines) == 1
    events[name] = json.loads(lines[0].split(f' {name} ', 1)[1])
assert events['HeartbeatTimedOut']['ElapsedMilliseconds'] >= 5000
assert events['HeartbeatRecovered']['RecoveryCount'] == 1
assert 'BrokerDisconnected' in monitor and 'ExitCode=1' in monitor
assert 'ConnectionLost' in receiver and 'ExitCode=1' in receiver
assert 'ExitCode=0' in fresh and 'SubscriptionStopped' in fresh and 'Disposed' in fresh
assert re.search(r'ClientId=(\S+)', receiver)[1] != re.search(r'ClientId=(\S+)', fresh)[1]
requests = json.loads(read('command-requests.json'))
wire = [json.loads(line.split(' ', 1)[1]) for line in read('command-observer.stdout.log').splitlines() if line.startswith('amr/DEMO_AMR_001/command {')]
assert len(wire) == 3 and len({m['cmdId'] for m in wire}) == 3
for request in requests:
    message = next(m for m in wire if m['cmdId'] == request['cmdId'])
    assert message['type'] == request['type']
    assert message['param'] == ({'x': 10, 'y': 20} if request['type'] == 'MOVE' else {})
for filename in ['receiver.stderr.log', 'monitor.stderr.log', 'fresh.stderr.log', 'command-MOVE.stderr.log', 'command-STOP.stderr.log', 'command-RESET.stderr.log']:
    assert not read(filename), filename
before = [json.loads(line) for line in read('containers-before.jsonl').splitlines()]
after = [json.loads(line) for line in read('containers-after.jsonl').splitlines()]
assert sorted((c['ID'], c['State']) for c in before) == sorted((c['ID'], c['State']) for c in after)
sources = json.loads(read('source-files.json'))
assert all(hashlib.sha256((REPO / s['path']).read_bytes()).hexdigest().upper() == s['sha256'] for s in sources)
audit = {'passed': True, 'checks': ['17 demo checks passed', '7 delivered messages: 6 initial and 1 recovery', 'original telemetry values and ACK fields retained', '1 invalid message rejected without extra delivery', 'three command payloads and request IDs matched real Broker observer', 'one heartbeat timeout and recovery', 'Broker loss exit 1, explicit fresh session, normal exit 0', 'existing container set and states unchanged', 'source and executable hashes match'], 'heartbeatTimeoutObservedMs': events['HeartbeatTimedOut']['ElapsedMilliseconds']}
(ROOT / 'report-audit.json').write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding='utf-8')

command_log = '\n\n'.join(f'[{name}]\n{read(f"command-{name}.stdout.log")}' for name in ['MOVE', 'STOP', 'RESET'])
command_log += '\n\n[MQTT 수신 측 원문]\n' + selected(read('command-observer.stdout.log'), r'^amr/')
items = [
    dict(id='connect', label='서버 접속', title='서버에 연결하고 네 종류의 메시지를 구독했다.', purpose='데이터를 받을 준비가 되었는지 확인한다.', input='별도 시험용 Mosquitto 서버에 콘솔 수신 프로그램을 실행했다.', result='서버 연결과 Heartbeat·Telemetry·Alarm·ACK 구독이 완료되었다.', log=selected(receiver, r' Settings | Connecting| Connected| Subscribing| Subscribed|WaitingForShutdown'), source='receiver.stdout.log'),
    dict(id='receive', label='메시지 수신', title='두 장비의 메시지를 구분해 수신했다.', purpose='어느 장비가 보낸 어떤 데이터인지 확인한다.', input='두 장비의 생존 신호·위치 데이터와 시험용 알람·응답을 MQTT로 발행했다.', result='장비 001: 위치 (1.25, 2.5), 속도 0.5, BUSY / 장비 002: 위치 (3, 4), 속도 0, IDLE. 네 메시지 종류를 모두 검사·전달했다.', log='\n\n'.join(json.dumps(m, ensure_ascii=False, indent=2) for m in initial), source='receiver.stdout.log', note='알람과 명령 응답은 통신 검사용 시험 데이터다. 가상 AMR이 실제 장애나 명령 수행으로 만든 결과는 아니다.'),
    dict(id='validation', label='잘못된 메시지', title='필수 시각이 빠진 메시지를 거부했다.', purpose='잘못된 데이터가 정상 상태로 전달되지 않는지 확인한다.', input=json.dumps({'id': 'DEMO_AMR_001', 'status': 'OK'}, ensure_ascii=False), result='필수 항목 ts가 없어 MissingField 오류로 거부했다. 해당 입력의 정상 전달은 없었고, 이후 정상 메시지는 계속 수신했다.', log=selected(receiver, r'Validation=Rejected| Rejected |"Timestamp":200'), source='receiver.stdout.log'),
    dict(id='commands', label='명령 전달', title='이동·정지·복구 명령을 MQTT 수신 측에서 확인했다.', purpose='요청한 대상·명령·요청 ID·좌표가 전송되는지 확인한다.', input='DEMO_AMR_001에 MOVE (10, 20), STOP, RESET을 각각 발행했다.', result='세 명령의 종류·요청 ID·입력값이 실제 MQTT 수신 원문과 일치했다.', log=command_log, source='command-observer.stdout.log', note='명령 전달 확인까지다. AMR의 실제 이동·정지·복구 수행 및 원래 요청과 ACK를 연결하는 기능은 미구현이다.'),
    dict(id='heartbeat', label='생존 신호 감시', title='신호 중단을 감지하고 재수신 후 복구를 확인했다.', purpose='서버 연결을 유지한 상태에서 특정 AMR의 응답 여부를 확인한다.', input='장비 001의 정상 생존 신호를 보낸 뒤 중단하고, 다시 정상 신호를 보냈다.', result=f'감시 시작 → 무응답 감지 → 생존 확인 복구. 기준은 5초, 이번 무응답 관측은 약 {events["HeartbeatTimedOut"]["ElapsedMilliseconds"] / 1000:.2f}초였다.', log=selected(monitor, r'HeartbeatMonitoringStarted|HeartbeatTimedOut|HeartbeatRecovered'), source='monitor.stdout.log', note='생존 신호 재수신에 따른 복구다. 끊어진 MQTT 서버에 자동 재접속한 결과가 아니다.'),
    dict(id='disconnect', label='연결 끊김·새 실행', title='서버 단절을 감지하고, 새 실행으로 다시 접속했다.', purpose='서버 단절과 정상 종료를 구분하고, 서버 복구 후 새 연결이 가능한지 확인한다.', input='시험용 서버 중단 → 기존 프로그램 종료 → 서버 시작 → 새 콘솔 프로그램 실행 → 정상 종료.', result='단절 시 기존 수신·감시 프로그램은 종료 코드 1로 끝났다. 서버 복구 후 명시적으로 새 프로그램을 실행해 수신했고, Ctrl+C 종료 코드 0을 확인했다.', log='[기존 수신 프로그램]\n' + selected(receiver, r'ConnectionLost|ShutdownRequested|QueueStopped|SubscriptionStopped|Disposed|ExitCode') + '\n\n[기존 감시 프로그램]\n' + selected(monitor, r'Failed|Closed|ExitCode') + '\n\n[명시적으로 새로 실행한 프로그램]\n' + selected(fresh, r' Settings | Connected| Subscribed| Received |ShutdownRequested|SubscriptionStopped|Disposed|ExitCode'), source='fresh.stdout.log', note='자동 재접속·재구독은 아직 미구현이다. 이 시연은 서버 복구 후 새 프로그램을 직접 실행한 결과다.'),
]
data = json.dumps(items, ensure_ascii=False).replace('</', '<\\/')
page = '''<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>과제 1 · 실제 실행 결과</title>
<style>
:root{font-family:"Segoe UI","Malgun Gothic",sans-serif;color:#17263a;background:#f2f5f9;font-size:15px}*{box-sizing:border-box}body{margin:0}main{max-width:1240px;margin:0 auto;padding:36px 28px 26px}a{color:#275cb7;text-decoration:none}a:hover{text-decoration:underline}.eyebrow{color:#68798f;font-size:12px;font-weight:700;letter-spacing:.07em}.top{display:flex;justify-content:space-between;align-items:flex-start;gap:20px}h1{font-size:30px;letter-spacing:-1px;margin:9px 0 8px}p{line-height:1.7;margin:0}.sub{color:#64758a}.badge{display:inline-block;border-radius:6px;padding:7px 11px;white-space:nowrap;font-weight:700;font-size:12px;background:#e2f2e9;color:#19633f}.badge.pending{background:#fff1d8;color:#8b5b13;margin-top:7px}.stats{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin:24px 0}.stat{border:1px solid #dce3ec;background:white;border-radius:10px;padding:17px 19px}.stat label{font-size:12px;color:#65778d}.stat strong{display:block;font-size:23px;margin-top:7px}.stat small{color:#68798b}.layout{display:grid;grid-template-columns:210px minmax(0,1fr);gap:18px}nav{background:white;border:1px solid #dce3ec;border-radius:11px;padding:16px 10px;height:fit-content}nav .navtitle{font-size:12px;color:#6b7b90;padding:0 10px 13px}nav button{display:flex;align-items:center;gap:12px;width:100%;padding:13px 10px;border:0;border-radius:7px;background:none;text-align:left;color:#4a5b70;font:inherit;cursor:pointer;margin-bottom:3px}nav button[aria-current=true]{background:#eaf0fd;color:#2453a0;font-weight:700}nav button span{font-size:11px;color:#8392a6}.panel{background:white;border:1px solid #dce3ec;border-radius:11px;overflow:hidden}.paneltop{padding:23px 25px 18px;border-bottom:1px solid #e7ecf2}.sectionlabel{color:#4773b5;font-size:12px;font-weight:700}h2{font-size:21px;letter-spacing:-.55px;margin:9px 0 6px}.purpose{color:#6b7b8e;font-size:13px}.info{display:grid;grid-template-columns:42px minmax(0,1fr);gap:10px 12px;padding:18px 25px;font-size:13px;line-height:1.7}.info dt{color:#6b7b90;font-weight:700}.info dd{margin:0;white-space:pre-wrap}.note{margin:0 25px 18px;background:#fff7e8;border-left:3px solid #d69d3e;padding:10px 13px;font-size:12px;color:#855e25;line-height:1.7}.consoleheader{display:flex;justify-content:space-between;background:#172539;color:#afbed2;font-size:11px;padding:10px 17px}.consoleheader a{color:#a9c9ff}pre{margin:0;background:#101b2c;color:#dbe7f5;padding:18px;font:12px/1.75 Consolas,"Malgun Gothic",monospace;white-space:pre-wrap;overflow-wrap:anywhere;max-height:470px;overflow:auto}.bottom{display:grid;grid-template-columns:1fr 1fr;gap:18px;margin-top:19px}.box{background:white;border:1px solid #dce3ec;border-radius:10px;padding:18px 21px}.box h3{font-size:14px;margin:0 0 9px}.box p{font-size:12px;color:#66768a}.box a{margin-right:12px}.footer{margin-top:18px;font-size:11px;color:#7a899c;line-height:1.8}.meta{font-size:11px;color:#7a899c;margin-top:8px}@media(max-width:800px){main{padding:22px 14px}.stats{grid-template-columns:1fr 1fr}.layout{grid-template-columns:1fr}nav{display:flex;flex-wrap:wrap;gap:4px}nav .navtitle{width:100%;padding-bottom:7px}nav button{width:auto;padding:9px}.bottom{grid-template-columns:1fr}.top{display:block}.top aside{margin-top:12px}.paneltop{padding:18px}h1{font-size:25px}h2{font-size:18px}.info{padding:16px 18px}.note{margin-left:18px;margin-right:18px}}
</style></head><body><main>
<div class="top"><header><div class="eyebrow">가상 AMR 관제 시스템 / 과제 1</div><h1>통신 서버·클라이언트 실제 실행 결과</h1><p class="sub">콘솔 프로그램의 저장된 출력을 항목별로 확인합니다.</p><div class="meta">__TIME__ KST · 코드 __CODE__ · .NET SDK __SDK__</div></header><aside><div class="badge">이번 시연 17 / 17 확인</div><br><div class="badge pending">과제 1 전체는 진행 중</div></aside></div>
<section class="stats"><div class="stat"><label>메시지 검사·수신</label><strong>4종류</strong><small>생존 신호 · 위치/상태 · 알람 · 응답</small></div><div class="stat"><label>실제 MQTT 명령 전달</label><strong>3종류</strong><small>MOVE · STOP · RESET</small></div><div class="stat"><label>생존 신호 중단·복구</label><strong>확인</strong><small>5초 기준으로 무응답 감시</small></div><div class="stat"><label>자동 재접속·수신 재개</label><strong style="color:#9b691a">미구현</strong><small>새 프로그램 실행으로 접속 확인</small></div></section>
<section class="layout"><nav id="nav" aria-label="실행 결과 선택"><div class="navtitle">확인할 결과를 선택하세요</div></nav><article class="panel"><div class="paneltop"><div class="sectionlabel" id="step"></div><h2 id="title"></h2><p class="purpose" id="purpose"></p></div><dl class="info"><dt>입력</dt><dd id="input"></dd><dt>결과</dt><dd id="result"></dd></dl><div class="note" id="note" hidden></div><div class="consoleheader"><span>실제 콘솔 출력 · 저장 기록</span><a id="source" target="_blank">원본 로그 열기 ↗</a></div><pre id="log" tabindex="0"></pre></article></section>
<section class="bottom"><article class="box"><h3>이번에 확인한 범위</h3><p>별도 Mosquitto 서버에서 실제 송수신·메시지 거부·명령 발행·생존 신호 감시·서버 단절·새 실행·정상 종료를 확인했습니다. 시험 서버와 프로그램은 정리했고 기존 컨테이너는 유지했습니다.</p><p style="margin-top:10px"><a href="verification.json" target="_blank">17개 항목 판정</a><a href="report-audit.json" target="_blank">원본 대조 결과</a><a href="README.md" target="_blank">실행 근거</a></p></article><article class="box"><h3>남은 구현·검증</h3><p>자동 재접속·재구독·수신 재개, 보낸 요청과 ACK 연결, 과제 1 전체 검증이 남아 있습니다. 알람·ACK 수신 검사는 시험 메시지를 사용했으며, 가상 AMR의 실제 장애·명령 수행을 확인한 결과는 아닙니다.</p></article></section>
<footer class="footer">독립된 시험 환경의 실행 기록을 보여주는 보기본입니다. 새로운 관제 GUI를 구현한 화면은 아닙니다.<br>실행 ID: __RUNID__ · 로그 시각은 UTC(+00:00), 상단 실행 시각은 KST(+09:00)입니다. 재실행해도 이 기록은 바뀌지 않습니다.</footer>
</main><script id="data" type="application/json">__DATA__</script><script>
const items=JSON.parse(document.getElementById('data').textContent);const nav=document.getElementById('nav');
function show(index){const item=items[index];for(const button of nav.querySelectorAll('button'))button.setAttribute('aria-current',String(Number(button.dataset.index)===index));document.getElementById('step').textContent=`${String(index+1).padStart(2,'0')} / ${item.label}`;for(const key of ['title','purpose','input','result','log'])document.getElementById(key).textContent=item[key];const note=document.getElementById('note');note.hidden=!item.note;note.textContent=item.note||'';document.getElementById('source').href=item.source;document.getElementById('log').scrollTop=0;}
items.forEach((item,index)=>{const button=document.createElement('button');button.type='button';button.dataset.index=index;const number=document.createElement('span');number.textContent=String(index+1).padStart(2,'0');button.append(number,document.createTextNode(item.label));button.addEventListener('click',()=>show(index));nav.append(button)});show(1);
</script></body></html>'''
replacements = {'__TIME__': verification['started'][:16].replace('T', ' '), '__CODE__': verification['code'][:7], '__SDK__': verification['sdk'], '__RUNID__': verification['runId'], '__DATA__': data}
for key, value in replacements.items():
    page = page.replace(key, value)
(ROOT / 'index.html').write_text(page, encoding='utf-8')

readme = f'''# 과제 1 실제 실행 결과

- 목적: 현재 통신 콘솔을 직접 실행하고 과제 1 결과물의 확인 가능한 부분을 보여준다.
- 보기본: [항목별 결과 화면](index.html). 제품 GUI가 아니라 실제 콘솔 출력의 읽기용 보기본이다.
- 실행 시각: {verification['started']} ~ {verification['ended']}.
- 코드: develop `{verification['code']}`, 제품 소스 변경 없음. 새 실행 근거만 생성했다.
- 환경: {verification['os']}, SDK {verification['sdk']}, MQTTnet 5.2.0.1603, Mosquitto 2.1.2-alpine. 실제 Docker 엔진은 [버전 원문](docker-server.json)을 따른다.
- 콘솔 빌드: [build.log](build.log), 경고 0 / 오류 0. 기존 자동시험 전체 재실행 없음.
- 실제 시연: [verification.json](verification.json)의 17항목 모두 확인. 입력은 [inputs.json](inputs.json), 외부 프로세스 명령은 [commands.jsonl](commands.jsonl), 소스·실행 DLL 해시는 [source-files.json](source-files.json).

| 시험 | 입력과 예상 결과 | 실제 결과·근거 |
|---|---|---|
| 접속·수신 | 별도 Broker 접속, 4유형을 장비별 전달 | [receiver.stdout.log](receiver.stdout.log), 2대의 Heartbeat/Telemetry와 시험 Alarm/ACK 수신 |
| 형식 검사 | ts 없는 Heartbeat는 거부, 정상 데이터 전달은 유지 | MissingField / $.ts 1건, 정상 전달 6건 + 복구 Heartbeat 1건. [원본 대조](report-audit.json) |
| 명령 발행 | MOVE(10,20)/STOP/RESET의 대상·ID·입력값 수신 일치 | 3건 일치. [발행 요청](command-requests.json), [실제 Broker 수신](command-observer.stdout.log) |
| 생존 감시 | 5초 신호 중단 시 무응답, 재수신 시 복구 | 1회 무응답·1회 복구, 관측 {events['HeartbeatTimedOut']['ElapsedMilliseconds'] / 1000:.3f}초. [monitor.stdout.log](monitor.stdout.log) |
| Broker 단절 | 서버 중단 시 감지·실패 종료 | 기존 receiver/monitor 모두 종료 코드 1 |
| 서버 재시작 후 새 실행 | 명시적으로 새 콘솔 실행, 수신 후 Ctrl+C 정상 종료 | [fresh.stdout.log](fresh.stdout.log), [정상 종료 신호/신원](normal-stop.json), 종료 코드 0 |
| 정리 | 소유 Broker/프로세스 제거, 기존 컨테이너 보존 | 기존 3컨테이너 ID·상태 동일, 추가 컨테이너 없음. before/after.jsonl |

## 한계·남은 작업

- 자동 재접속·재구독은 미구현이다. Broker 재시작 후 새 프로그램을 실행한 시험과 구분한다.
- Alarm/ACK는 mosquitto_pub으로 만든 통신 검사 입력이다. 실제 가상 AMR의 장애나 명령 수행 응답이 아니다.
- 보낸 요청과 ACK를 연결하는 기능은 미구현이다. 이번 명령 검증은 Broker 관찰 수신까지다.
- 장시간·대량 입력·과제1 전체 완료조건 검증은 이번에 수행하지 않았다.
- 별도 Broker는 127.0.0.1 임의 포트, persistence=false, --pull never, 실행 ID 라벨로 소유권을 확인했다. 기존 프로젝트 Broker·Seq를 시작하거나 중단하지 않았다.

## 재현

저장소 루트에서 .NET 콘솔을 빌드하고, 현재 폴더의 run-task1-demo.ps1과 같은 절차를 새 실행 ID로 수행한다. 이 파일은 이번 실행 절차의 고정본이며 동일 실행 폴더를 덮어쓰지 않는다. 재실행 전 새 폴더에서 콘솔 빌드 출력을 build.log로 남겨야 한다. Docker Linux 엔진과 캐시된 Mosquitto 이미지가 필요하다. 제품 소스·실행 DLL·입력값은 위 해시와 실행 명령으로 식별한다.
'''
(ROOT / 'README.md').write_text(readme, encoding='utf-8')
print(json.dumps({'report': str(ROOT / 'index.html'), 'auditPassed': True, 'demoChecks': 17, 'receivedMessages': len(received)}, ensure_ascii=False))
