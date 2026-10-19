# COM01-B 빌드·자동시험 01

기준 e40e2b4 + feature/21-multi-amr-subscription 미커밋 변경. [소스 해시](source-version.json)와 [환경](environment.log)을 기록했다.

빌드 경고·오류 0. 기존 463 + 신규 수신부 66 = 529건 모두 통과(실패·건너뜀 0). [빌드 로그](build.log), [시험 로그](test.log), [TRX](tests.trx). 검사기·Fake 시험이며 Broker 연동 근거는 [실제 MQTT 실행 03](../20261019-multi-mqtt-03/README.md)에 별도로 연결한다.
