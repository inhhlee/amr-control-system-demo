# MQTT 시험 도구 두 번째 시도 — 실패 보존

- 실행: `pwsh -NoProfile -File evidence/COM-01/20261012-heartbeat-03/verify-mqtt.ps1`, 종료 1.
- C# 콘솔은 실제 MQTT 접속·Heartbeat SUBACK을 확인했다. [콘솔 로그](console-1.stdout.log).
- 독립 Mosquitto 관찰자는 출력 리디렉션의 버퍼링 때문에 실행 중 SUBACK 문자열을 읽을 수 없어 시험 도구가 10초 대기 후 중단했다. 종료 후 [관찰자 로그](observer-1.stdout.log)에는 SUBACK이 있다. 실제 시험 발행 전이므로 수신·필터·Ctrl+C 통과로 집계하지 않는다.
- 소유한 C# 시험 프로세스와 Docker CLI만 정리했다. 관찰자 프로세스는 Mosquitto의 `-W 15` 제한으로 종료했다. Broker 서비스는 유지했다.
- 재시험에서는 해당 관찰자에 Docker exec TTY를 지정해 줄 단위 출력을 읽는다. 콘솔의 리디렉션 로그도 UTF-8로 고정한다. 재시험 ID: `20261012-heartbeat-04`.
