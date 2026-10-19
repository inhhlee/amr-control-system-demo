# COM04-B Validation 폴더 정리 검증

- 실행 ID: `20261019-validation-layout-01`, Codex, 2026-10-19.
- 사용자 승인 범위: 기존 Validation 파일을 Common·Heartbeat·Telemetry·Alarm으로 분류한다. namespace·내용은 유지한다. Ack 폴더와 AlarmValidator는 이번에 추가하지 않는다.
- 기준: Issue #18 COM04-B의 기존 수신 계약 유지. 새로운 데이터 정책이나 검사 규칙 변경 없음.
- 코드: `feature/18-message-validation`, 기준 커밋 `0a4de95d8e582bd14cafe337e0d8c5389da67b4a` + 기존 미커밋 변경을 보존한 파일 이동. [실행 시 전체 소스 해시](source-version.json).
- 환경: Windows, .NET SDK 10.0.401, net10.0. [실제 환경](environment.log).

## 변경

| 폴더 | 이동 파일 |
| --- | --- |
| Validation/Common | MessageValidation.cs, ValidationError.cs |
| Validation/Heartbeat | HeartbeatValidator.cs, HeartbeatValidationResult.cs |
| Validation/Telemetry | TelemetryValidator.cs, TelemetryValidationResult.cs |
| Validation/Alarm | AlarmValidationResult.cs |

Models·시험 코드·프로젝트 파일·namespace·공개 API는 이번 이동에서 변경하지 않았다. [이동 경로와 이동 전 SHA256](file-moves.json)을 새 위치의 파일과 비교했다. README와 이전 공통 필드 검사 증거 README의 소스 링크만 현재 경로로 갱신했고, 과거 실행의 원본 경로·해시·로그·TRX는 유지했다.

폴더 생성은 첫 시도에서 샌드박스 권한 오류로 차단되어 파일 이동 전에 중단됐다. 같은 이동 목록과 해시를 사용해 권한을 높여 재시도했다. 빌드·시험도 앞선 실행에서 확인한 obj 쓰기 제한 때문에 권한을 높여 수행했다.

## 예상·실제·판정

| 확인 | 예상 | 실제 |
| --- | --- | --- |
| 7개 파일 이동 | 이전 위치 없음, 새 위치 존재, 바이트 내용 동일 | 7개 SHA256 일치, 통과 |
| 직전 실행 대비 소스 | 경로 이동 이외 코드·시험·프로젝트 변경 없음 | 직전 스냅샷 27개 파일 모두 경로 대응 후 해시 일치, 통과 |
| 솔루션 빌드 | 새 하위 폴더의 소스 자동 포함, 컴파일 성공 | 경고 0·오류 0·종료 0, 통과 |
| 기존 자동시험 | 240개 기존 결과 유지 | 통과 240·실패 0·건너뜀 0·종료 0 |

- [기계 검증 결과](verification.json), [빌드 로그](build.log), [빌드 명령·종료 코드](build-result.json).
- [시험 로그](test.log), [TRX](tests.trx), [시험 명령·완료 시각](test-result.json).
- 입력·예상값은 기존 tests/Amr.Communication.Tests의 시험에 정의되어 있으며 TRX 시험명과 source-version.json의 파일 해시로 연결한다. 이전 [공통 필드 검사 실행](../20261019-alarm-fields-01/README.md)의 시험 구성을 그대로 재실행했다.

저장소 루트에서 `dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal`, 이어서 `dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --no-restore`로 재현한다. 로그를 저장할 때는 새 실행 ID를 사용한다.

이번 결과는 폴더 정리·컴파일·MQTT 없는 자동 회귀 확인이다. Alarm 값 검사기와 ACK는 미구현이며 실제 MQTT·GUI·Core·큐는 미실행이다. #18 전체 완료로 판정하지 않는다. 커밋·푸시·PR·병합은 수행하지 않았다.
