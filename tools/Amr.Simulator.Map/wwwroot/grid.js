(() => {
  'use strict';
  const el = id => document.getElementById(id);
  const same = (a, b) => a.row === b.row && a.col === b.col;
  let snapshot = null, connected = false, pending = false;
  const cells = [];
  let gridShape = '';
  function buildGrid(map) {
    const shape = `${map.length}x${map[0].length}`;
    if (shape === gridShape) return;
    gridShape = shape;
    cells.length = 0;
    el('navigation-grid').replaceChildren();
    el('navigation-grid').style.gridTemplateColumns = `repeat(${map[0].length}, minmax(0, 1fr))`;
    for (let row = 0; row < map.length; row++) {
      for (let col = 0; col < map[row].length; col++) {
        const button = document.createElement('button');
        button.className = 'grid-cell';
        button.dataset.row = row; button.dataset.col = col;
        button.innerHTML = '<small></small><span></span>';
        button.firstChild.textContent = `(${row}, ${col})`;
        button.addEventListener('click', () => {
          if (snapshot) action('obstacle', { row, col, blocked: snapshot.map[row][col] === 0 });
        });
        el('navigation-grid').append(button);
        cells.push(button);
      }
    }
  }
  function controls() {
    const active = snapshot && ['Connecting', 'Running', 'Waiting', 'WaitingForTelemetry', 'Canceling', 'Resetting'].includes(snapshot.phase);
    el('grid-start').disabled = !connected || pending || active;
    el('grid-cancel').disabled = !connected || pending || !active || ['Canceling', 'Resetting'].includes(snapshot.phase);
    el('grid-reset').disabled = !connected || pending || snapshot?.phase === 'Resetting';
    if (!snapshot) return;
    for (const button of cells) {
      const cell = { row: Number(button.dataset.row), col: Number(button.dataset.col) };
      button.disabled = !connected || pending || snapshot.fixedObstacles[cell.row][cell.col] ||
        same(cell, snapshot.current) || same(cell, snapshot.start) || same(cell, snapshot.goal);
    }
  }
  function render(data) {
    snapshot = data.navigation;
    const s = snapshot;
    buildGrid(s.map);
    el('grid-map-size').textContent = `${s.map.length}행 × ${s.map[0].length}열 · 상하좌우 이동 · 1칸 = 1m`;
    el('grid-settings').textContent = `한 칸 이동 ${data.stepIntervalMs / 1000}초 · 설정: ${data.configurationPath || '직접 생성'} · 파일 변경 후 서버를 다시 시작해 주세요.`;
    const mqtt = data.source === 'mqtt-grid-control-receiver';
    const receivedPosition = data.position;
    const phases = { Ready: '출발 준비', Connecting: 'MQTT 연결 중', Running: '이동 중', Waiting: '길이 막혀 대기 중', WaitingForTelemetry: '도착 수신 확인 중', Completed: mqtt ? '도착 · 수신 확인' : '도착 · 정지', Canceled: '이동 중단됨', Canceling: '종료 처리 중', Resetting: '초기화 중', Failed: '송수신 오류 · 정지' };
    el('grid-phase').textContent = phases[s.phase] || s.phase;
    el('grid-status').textContent = s.phase === 'Failed' ? `송수신이 중단되어 이동을 멈췄습니다. ${receivedPosition ? '마지막 수신 위치를 표시합니다.' : '수신한 위치가 없어 AMR을 표시하지 않습니다.'} 사유: ${data.failure || '확인 필요'}`
      : s.phase === 'Connecting' ? '관제가 먼저 구독한 뒤 AMR이 위치 송신을 시작합니다.'
      : s.phase === 'WaitingForTelemetry' ? '목적지 좌표·IDLE·속도 0을 관제에서 받았는지 확인합니다.'
      : ['Canceling', 'Resetting'].includes(s.phase) ? '송수신 작업 종료와 연결 정리를 기다리고 있습니다.'
      : s.phase === 'Waiting' ? '현재 순서의 경유점까지 갈 수 없어 대기합니다. 장애물을 제거하면 같은 순서로 다시 출발합니다.'
      : s.phase === 'Completed' ? mqtt ? `경유점을 순서대로 방문한 뒤 관제가 목적지 (${s.goal.row}, ${s.goal.col})와 IDLE·속도 0을 수신했습니다.` : '경유점을 모두 방문하고 목적지에서 정지했습니다.'
      : s.phase === 'Canceled' ? '이동을 중단했습니다. 다시 출발하면 시작점에서 실행합니다.'
      : s.remainingDistance === null ? '길이 막혀 있습니다. 장애물을 제거하거나 출발 후 길이 열리기를 기다릴 수 있습니다.'
      : '같은 AMR이 현재 위치에서 다음 경유점까지의 최단 경로를 찾습니다.';
    el('grid-position-label').textContent = mqtt ? s.phase === 'Failed' || s.phase === 'Canceled' ? '마지막 수신 (행, 열)' : '수신 (행, 열)' : '현재 (행, 열)';
    el('grid-position').textContent = receivedPosition ? `(${receivedPosition.row}, ${receivedPosition.col})` : '수신 대기';
    el('grid-distance').textContent = s.remainingDistance === null ? '경로 없음' : `${s.remainingDistance}칸`;
    el('grid-target').textContent = s.currentTarget ? `${s.nextWaypointIndex === s.waypoints.length - 1 ? 'G' : `W${s.nextWaypointIndex + 1}`} (${s.currentTarget.row}, ${s.currentTarget.col})` : '모두 방문';
    el('grid-progress').textContent = `${s.nextWaypointIndex} / ${s.waypoints.length}`;
    el('grid-route-summary').textContent = `S (${s.start.row}, ${s.start.col}) → ${s.waypoints.map((p, i) => `${i === s.waypoints.length - 1 ? 'G' : `W${i + 1}`} (${p.row}, ${p.col})`).join(' → ')}`;
    el('grid-waypoints').replaceChildren(...s.waypoints.map((p, i) => {
      const item = document.createElement('li');
      item.className = i < s.nextWaypointIndex ? 'mission-reached' : i === s.nextWaypointIndex ? 'mission-current' : '';
      item.textContent = `${i === s.waypoints.length - 1 ? '목적지' : `경유점 ${i + 1}`} (${p.row}, ${p.col}) · ${i < s.nextWaypointIndex ? '방문 완료' : i === s.nextWaypointIndex ? '현재 목표' : '방문 예정'}`;
      return item;
    }));
    el('grid-moves').textContent = `${s.moves}회`;
    el('grid-replans').textContent = `${s.replans}회`;
    el('grid-start').textContent = s.phase === 'Ready' ? '출발' : '다시 출발';
    el('grid-path').textContent = s.path.length ? `남은 경로: ${s.path.map(p => `(${p.row}, ${p.col})`).join(' → ')}` : '남은 경로: 없음';
    el('grid-evidence').textContent = `전체 이동 기록: ${data.evidencePath}`;
    el('grid-logs').textContent = data.logs.join('\n');
    el('source-title').textContent = mqtt ? '단일 AMR · 경유점과 장애물 우회 · MQTT 관제 수신' : '단일 AMR · 경유점과 장애물 우회';
    el('source-chain').textContent = mqtt ? '경유점 순서대로 우회 이동 → MQTT → 관제의 메시지 검사·큐 → 수신 좌표로 지도 표시' : '같은 AMR이 경유점을 순서대로 방문하면서 장애물을 피해 이동합니다.';
    el('grid-source').textContent = mqtt ? `AMR 표시와 이동 흔적은 관제가 수신한 Telemetry만 사용합니다. 1칸=1m, X=열·Y=-행으로 변환합니다. 경로·이동 횟수는 격자 시뮬레이터의 계산 결과입니다. AMR: ${data.amrId}` : '서버가 계산한 경로와 이동 결과입니다. MQTT를 사용하지 않는 직접 연결 모드입니다.';
    el('grid-mqtt-metrics').hidden = !mqtt;
    el('grid-mqtt-metrics').textContent = mqtt ? `관제 위치 수신 ${data.telemetryCount}건 · Heartbeat ${data.heartbeatCount}건 · 거부 ${data.rejectedCount}건\n수신 상태 ${data.receivedTelemetry?.state || '대기'} · 속도 ${data.receivedTelemetry ? Number(data.receivedTelemetry.speed.toFixed(3)) : '—'} m/s · Broker ${data.mqttEndpoint}` : '';
    el('grid-received').hidden = !mqtt;
    el('grid-received').textContent = data.receivedTelemetry ? JSON.stringify(data.receivedTelemetry, null, 2) : '수신한 Telemetry가 없습니다.';
    for (const button of cells) {
      const p = { row: Number(button.dataset.row), col: Number(button.dataset.col) };
      const fixed = s.fixedObstacles[p.row][p.col], blocked = s.map[p.row][p.col] === 1;
      const current = receivedPosition !== null && same(p, receivedPosition), start = same(p, s.start), goal = same(p, s.goal);
      const routeIndex = s.path.findIndex(x => same(x, p));
      const waypointIndexes = s.waypoints.map((w, i) => same(w, p) ? i : -1).filter(i => i >= 0);
      const waypointIndex = waypointIndexes.find(i => i >= s.nextWaypointIndex) ?? waypointIndexes.at(-1);
      const waypoint = waypointIndex !== undefined && waypointIndex < s.waypoints.length - 1;
      const visited = (mqtt ? data.receivedTrail : s.traveled).some(x => same(x, p));
      button.className = `grid-cell${fixed ? ' fixed' : blocked ? ' added' : ''}${routeIndex >= 0 ? ' on-route' : ''}${visited ? ' visited' : ''}${goal ? ' goal' : ''}${waypoint ? ' mission-point' : ''}${current ? ' current' : ''}`;
      button.lastChild.textContent = current ? 'AMR' : blocked ? '■' : goal ? 'G' : waypoint ? `W${waypointIndex + 1}` : start ? 'S' : routeIndex > 0 ? routeIndex : '';
      const kind = current ? '현재 AMR' : fixed ? '고정 벽' : blocked ? '추가 장애물, 누르면 제거' : goal ? '목적지' : start ? '출발점' : waypoint ? `경유점 ${waypointIndex + 1}, 누르면 장애물 추가` : '통로, 누르면 장애물 추가';
      button.setAttribute('aria-label', `${p.row}행 ${p.col}열 ${kind}`);
      button.title = `(${p.row}, ${p.col}) ${kind}`;
    }
    el('connection').textContent = mqtt ? s.phase === 'Failed' ? 'MQTT 오류 · 마지막 수신' : s.phase === 'Completed' ? 'MQTT 도착 수신 확인' : data.brokerState === 'Subscribed' ? 'MQTT 관제 수신 중' : 'MQTT 시연 준비·종료' : '격자 시연 연결됨';
    el('connection').className = s.phase === 'Failed' ? 'badge offline' : 'badge';
    controls();
  }
  async function action(name, body) {
    if (pending || !connected) return;
    pending = true; controls(); el('grid-error').textContent = '';
    try {
      const response = await fetch(`/api/grid/${name}`, { method: 'POST',
        headers: { 'X-Simulator-Map': '1', 'Content-Type': 'application/json' },
        body: body === undefined ? undefined : JSON.stringify(body) });
      if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.message || '요청을 처리하지 못했습니다. 상태를 확인하고 다시 시도하세요.');
      }
    } catch (error) { el('grid-error').textContent = error.message; }
    finally { pending = false; controls(); }
  }
  async function pollGrid() {
    try {
      const response = await fetch('/api/grid/state', { cache: 'no-store' });
      if (!response.ok) throw new Error('state');
      const data = await response.json(); connected = true; render(data);
    } catch {
      connected = false; controls();
      el('connection').textContent = '연결 끊김 · 마지막 위치';
      el('connection').className = 'badge offline';
      el('grid-status').textContent = '최신 위치를 확인할 수 없습니다. 지도 서버를 확인하세요.';
    } finally { setTimeout(pollGrid, 100); }
  }
  el('grid-start').addEventListener('click', () => action('start'));
  el('grid-cancel').addEventListener('click', () => action('cancel'));
  el('grid-reset').addEventListener('click', () => action('reset'));
  pollGrid();
})();
