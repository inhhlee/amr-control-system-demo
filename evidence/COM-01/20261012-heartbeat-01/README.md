# 첫 빌드 기록

- 기준 HEAD: `746c173d31ff9744e60a7e3be29fa253c98482c2` + 이번 미커밋 구현.
- `dotnet restore AmrControlSystem.slnx`: 종료 0. [restore.log](restore.log).
- `dotnet build AmrControlSystem.slnx --no-restore`: 종료 1. [build.log](build.log).
- 원인: 새 시험 코드에서 MQTTnet 발행 QoS 메서드와 Subscribe/Unsubscribe 결과 생성자 사용 오류 5개. 제품 라이브러리·콘솔은 빌드됐지만 솔루션 전체는 실패했다.
- 수정: 실제 5.2.0.1603 생성자와 `WithQualityOfServiceLevel` 적용. 재시험은 [02 기록](../20261012-heartbeat-02/build-test.json), 18개 자동시험 통과.
- 이 실행에서 실제 MQTT 시험은 수행하지 않았다.
