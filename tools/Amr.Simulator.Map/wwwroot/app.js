'use strict';
const byId = id => document.getElementById(id);
const ns = 'http://www.w3.org/2000/svg';
let state = null, connected = false, pending = false, layoutKey = '', project = null;
function fmt(value, digits = 3) { return Number.isFinite(value) ? (Math.abs(value) > 1e6 ? value.toExponential(2) : Number(value.toFixed(digits)).toString()) : '—'; }
function node(tag, attrs, text) {
  const el = document.createElementNS(ns, tag);
  for (const [key, value] of Object.entries(attrs)) el.setAttribute(key, value);
  if (text !== undefined) el.textContent = text;
  return el;
}
function drawMap(settings) {
  const points = [settings.initialPosition, ...settings.waypoints];
  const xs = points.map(p => p.x), ys = points.map(p => p.y);
  const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
  const span = Math.max(maxX - minX, maxY - minY, 2) * 1.5;
  const cx = minX / 2 + maxX / 2, cy = minY / 2 + maxY / 2;
  const lowX = cx - span / 2, lowY = cy - span / 2;
  byId('grid').replaceChildren(); byId('route').replaceChildren();
  if (![span, lowX, lowY, lowX + span, lowY + span].every(Number.isFinite)) {
    project = null; byId('robot').setAttribute('visibility', 'hidden');
    byId('map-note').textContent = '이 좌표 범위는 지도에 표시할 수 없습니다. 현재 좌표와 원본 로그를 확인하세요.';
    return;
  }
  project = p => ({ x: 200 + (p.x - lowX) / span * 400, y: 450 - (p.y - lowY) / span * 400 });
  for (let i = 0; i <= 6; i++) {
    const valueX = lowX + span * i / 6, valueY = lowY + span * i / 6;
    const px = 200 + i * 400 / 6, py = 450 - i * 400 / 6;
    byId('grid').append(node('line', { x1: px, y1: 50, x2: px, y2: 450, class: Math.abs(valueX) < 1e-9 ? 'axis' : 'grid-line' }),
      node('line', { x1: 200, y1: py, x2: 600, y2: py, class: Math.abs(valueY) < 1e-9 ? 'axis' : 'grid-line' }),
      node('text', { x: px, y: 476, class: 'tick', 'text-anchor': 'middle' }, fmt(valueX, 2)),
      node('text', { x: 183, y: py + 4, class: 'tick', 'text-anchor': 'end' }, fmt(valueY, 2)));
  }
  byId('grid').append(node('text', { x: 625, y: 476, class: 'tick' }, 'X (m)'), node('text', { x: 165, y: 30, class: 'tick' }, 'Y (m)'));
  byId('route').append(node('polyline', { points: points.map(p => { const xy = project(p); return `${xy.x},${xy.y}`; }).join(' '), class: 'route-line' }));
  points.forEach((p, i) => {
    const xy = project(p), label = i === 0 ? '출발' : i === points.length - 1 ? '목적지' : `경유점 ${i}`;
    byId('route').append(node('circle', { cx: xy.x, cy: xy.y, r: 7, class: 'waypoint', 'data-index': i - 1 }),
      node('text', { x: xy.x + 15, y: xy.y + 23, class: 'point-label' }, `${label} (${fmt(p.x)}, ${fmt(p.y)})`));
  });
  byId('map-note').textContent = '좌표 눈금은 m 단위입니다. 건물 도면이나 장애물 지도는 포함하지 않습니다.';
}
function buttons() {
  const active = state && ['Running', 'WaitingForTelemetry', 'Canceling'].includes(state.phase);
  byId('start').disabled = !connected || pending || active;
  byId('cancel').disabled = !connected || pending || !active || state.phase === 'Canceling';
}
function render(s) {
  state = s;
  const mqtt = s.source === 'mqtt-control-receiver';
  byId('source-title').textContent = mqtt ? '과제2 · MQTT 관제 수신 연결' : '과제2 · 실제 시뮬레이터 연결';
  byId('source-chain').textContent = mqtt ? '시뮬레이터 → MQTT Broker → 관제의 메시지 검사·큐 → 이 지도' : '설정한 경로를 따라 움직이는 AMR의 현재 위치를 보여드립니다.';
  const key = JSON.stringify(s.settings);
  if (key !== layoutKey) { drawMap(s.settings); layoutKey = key; }
  if (project && s.position) {
    const xy = project(s.position);
    byId('robot').setAttribute('visibility', 'visible');
    byId('robot').setAttribute('transform', `translate(${xy.x} ${xy.y})`);
    // 실제 수신 좌표를 검사할 수 있도록 보관한다. 보간·별도 이동 타이머는 없다.
    byId('robot').dataset.x = s.position.x; byId('robot').dataset.y = s.position.y;
    document.querySelectorAll('.waypoint').forEach(el => el.classList.toggle('passed', Number(el.dataset.index) < s.waypointsPassed));
  } else {
    byId('robot').setAttribute('visibility', 'hidden');
    delete byId('robot').dataset.x; delete byId('robot').dataset.y;
  }
  byId('amr-id').textContent = s.settings.amrId;
  const phases = { Ready: '출발 준비', Running: s.position ? '실행 중' : '수신 대기', WaitingForTelemetry: '도착 수신 확인 중', Canceling: '취소 처리 중', Canceled: '실행 취소됨', Completed: '도착 · 정지', Failed: '실행 오류' };
  byId('phase').textContent = s.phase === 'Running' && s.amrState === 'BUSY' ? '이동 중' : phases[s.phase] || s.phase;
  const notes = { Ready: mqtt ? '시연 시작을 누르면 관제가 먼저 구독하고 AMR이 출발합니다.' : '시연 시작을 누르면 출발합니다.', Running: mqtt ? '관제가 MQTT로 수신·검사한 위치와 상태입니다.' : '실제 시뮬레이터 좌표를 받고 있습니다.', WaitingForTelemetry: '마지막 도착 좌표가 관제에 도착했는지 확인합니다.', Canceling: '실행·송수신 작업이 끝날 때까지 기다립니다.', Canceled: mqtt ? '실행을 취소했습니다. 마지막 수신 위치를 표시합니다.' : '목적지 도착과 구분됩니다. 마지막 계산 좌표를 표시합니다.', Completed: mqtt ? '관제에서 목적지 좌표와 IDLE·속도 0을 받았습니다.' : '목적지에 도착해 속도가 0이 됐습니다.', Failed: s.receiveFailure === 'BrokerDisconnected' ? 'Broker 연결이 끊겼습니다. 마지막 수신 위치가 남아 있습니다.' : s.receiveFailure === 'FinalTelemetryNotReceived' ? '최종 도착 정보의 수신을 확인하지 못했습니다.' : '아래 실행 로그에서 오류를 확인하세요.' };
  byId('status-note').textContent = notes[s.phase];
  byId('position').textContent = s.position ? `(${fmt(s.position.x)}, ${fmt(s.position.y)})` : '수신 대기';
  byId('speed').textContent = fmt(s.speed); byId('elapsed').textContent = s.elapsedSeconds.toFixed(3);
  byId('amr-state').textContent = s.amrState || '수신 대기';
  byId('receive-metrics').hidden = !mqtt;
  byId('telemetry-count').textContent = s.telemetryCount;
  byId('heartbeat-count').textContent = s.heartbeatCount;
  byId('received-time').textContent = s.receivedTelemetry ? new Date(s.receivedTelemetry.receivedAtUtc).toLocaleTimeString('ko-KR', { hour12: false }) : '—';
  byId('received-payload').hidden = !mqtt;
  byId('received-payload').textContent = s.receivedTelemetry ? JSON.stringify(s.receivedTelemetry, null, 2) : '아직 MQTT 위치 정보를 받지 않았습니다.';
  byId('evidence-note').textContent = mqtt ? '지도 위치·속도·상태는 관제의 MQTT 수신 모델만 사용합니다. 아래에는 마지막 수신 모델과 실행 로그가 있습니다. 경과 시간은 시뮬레이터 실행 로그 기준이며, 화면은 약 100ms마다 조회합니다.' : '현재 콘솔과 같은 SimulatorApplication을 실행해 출력 좌표를 표시합니다. 이동을 따로 계산하지 않으며 약 100ms마다 조회합니다.';
  byId('exit-code').textContent = s.exitCode === null ? '—' : String(s.exitCode);
  byId('start').textContent = s.phase === 'Ready' ? '시연 시작' : '다시 실행';
  byId('logs').textContent = s.logs.length ? s.logs.join('\n') : '아직 실행하지 않았습니다.';
  byId('evidence').textContent = s.evidenceDirectory;
  byId('speech').textContent = s.phase === 'Completed'
    ? mqtt ? `“가상 AMR이 보낸 위치와 상태를 관제가 받아 지도에 표시했습니다. 목적지 (${fmt(s.position.x)}, ${fmt(s.position.y)})와 정지 상태 IDLE, 속도 ${fmt(s.speed)}을 수신한 것까지 확인했습니다.”` : `“실제 시뮬레이터가 설정한 경로를 따라 이동했습니다. ${s.elapsedSeconds.toFixed(3)}초 뒤 목적지 (${fmt(s.position.x)}, ${fmt(s.position.y)})에 도착했고, 현재 속도는 ${fmt(s.speed)}입니다.”`
    : s.phase === 'Canceled' ? '“실행을 취소했습니다. 지도에는 취소 전에 마지막으로 계산한 위치가 남아 있습니다.”'
    : mqtt ? '“시연 시작을 누르면 AMR이 위치를 MQTT로 보내고, 관제가 받은 값에 따라 점이 움직입니다. 목적지 좌표와 정지 상태가 수신되는지 확인하겠습니다.”' : '“시연 시작을 누르면 실제 시뮬레이터가 계산한 좌표에 따라 점이 움직입니다. 경유점을 지나 목적지에서 멈추는 모습을 확인하겠습니다.”';
  byId('connection').textContent = mqtt ? s.receiveFailure === 'BrokerDisconnected' ? 'Broker 연결 끊김 · 마지막 수신' : s.phase === 'Completed' ? 'MQTT 도착 수신 확인' : s.brokerState === 'Subscribed' ? 'MQTT 관제 수신 중' : 'MQTT 시연 준비·종료' : '실제 시뮬레이터 연결';
  byId('connection').className = s.phase === 'Failed' ? 'badge offline' : 'badge';
  buttons();
}
async function poll() {
  try {
    const response = await fetch('/api/state', { cache: 'no-store' });
    if (!response.ok) throw new Error('state');
    const data = await response.json();
    connected = true; render(data);
  } catch {
    connected = false; buttons();
    byId('connection').textContent = '연결 끊김 · 마지막 수신 위치'; byId('connection').className = 'badge offline';
    byId('status-note').textContent = '최신 상태를 확인할 수 없습니다. PowerShell의 지도 서버를 확인하세요.';
  } finally { setTimeout(poll, 100); }
}
async function action(name) {
  pending = true; buttons(); byId('action-error').textContent = '';
  try {
    const response = await fetch(`/api/${name}`, { method: 'POST', headers: { 'X-Simulator-Map': '1' } });
    if (!response.ok) throw new Error(response.status === 409 ? '아직 실행 또는 마무리 중입니다. 잠시 후 다시 눌러주세요.' : '요청을 처리하지 못했습니다. 서버 상태를 확인하세요.');
  } catch (error) { byId('action-error').textContent = error.message; }
  finally { pending = false; buttons(); }
}
byId('start').addEventListener('click', () => action('start'));
byId('cancel').addEventListener('click', () => action('cancel'));
if (new URLSearchParams(location.search).get('mode') !== 'grid') poll();
