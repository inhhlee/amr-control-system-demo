# 실제 Broker 시험 첫 시도 — 실패 보존

- 실행 명령: `pwsh -NoProfile -File evidence/COM-01/20261012-heartbeat-02/verify-mqtt.ps1`
- 실행 결과: 종료 1. `Wait-Log`의 `File.ReadAllText`가 리디렉션 중인 console-1.stdout.log를 열지 못했다(다른 프로세스에서 사용 중).
- 시험 도구의 파일 공유 방식 오류다. 이 시도의 MQTT 수신·Ctrl+C 결과를 통과로 집계하지 않는다.
- 해당 스크립트가 만든 시험 프로세스만 종료해 정리했다. [정리 기록](harness-failure.log).
- 수정: 실행 중인 로그를 `Get-Content -Raw`로 읽는다. 재시험은 `20261012-heartbeat-03`에서 수행한다.
- 앞선 빌드·자동시험은 별도 결과다: [명령·종료 코드](build-test.json), [빌드](build.log), [자동시험](test.log), [TRX](tests.trx).
