# 테스트 폴더 분류 검증 — 2026-10-20

- 요청: 파일 위치만 기능별 폴더로 분류하고 namespace·클래스명·시험 내용 유지.
- 기준: feature/26-serilog-seq, HEAD b4265aff198ead9d234c592cfaf9b89f9e217024 + 기존 #26 미커밋 구현. 새 Issue·브랜치·커밋·스테이징·푸시·PR은 수행하지 않았다. 이번 경로 정리는 #26 기능 변경과 구분한다.
- 대상: tests/Amr.Communication.Tests 루트 C# 파일 16개. 기존 csproj·패키지·프로젝트 참조는 변경하지 않았다.
- 분류: Mqtt/Connection(2), Mqtt/Subscriptions(2), Mqtt/Queues(3), Validation(6), Console(1), Console/Logging(1), TestDoubles(1).
- [이동 전후 경로·SHA-256](moves.json): 모든 대상이 테스트 프로젝트 내부인지 확인한 뒤 이동했으며 16개 모두 파일 바이트가 동일하다. namespace·클래스·시험 내용·개행을 포함한다.
- README의 현재 소스 링크 7곳만 갱신했다. 기존 사용자 변경, #26 구현, 과거 evidence의 소스 경로·해시·기록은 보존했다.

## 검증

환경: Windows / SDK 10.0.401 / net10.0 / 기존 프로젝트 패키지 유지. 세부 실행 시각·환경은 TRX와 실행 로그에 기록된다.

```powershell
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-restore --logger 'trx;LogFileName=tests.trx' --results-directory evidence/maintenance/20261020-test-folders-01
```

| 확인 | 예상 | 실제·판정 |
| --- | --- | --- |
| 파일 내용 보존 | 이동 전후 16개 SHA-256 동일 | 전부 동일, 통과 |
| 빌드·전체 자동시험 | 기존 580건 발견·통과 | 580 통과, 실패·건너뜀 0, 컴파일 경고 없음 |
| 시험 식별자 보존 | 이전 LOG seq-unit-03 TRX와 테스트 이름·중복 개수 동일 | Counter 비교 일치, 통과 |
| 현재 README 링크 | 이동된 소스를 가리킴 | 7개 링크 갱신 |

[실행 로그](test.log) · [TRX](tests.trx) · [기계 판정](verification.json)

실제 MQTT·Seq 연동은 이번 파일 이동에서 재실행하지 않았다. 서비스·컨테이너도 조작하지 않았다. #26 이전 실연동 결과는 해당 실행의 근거로 보존한다.
