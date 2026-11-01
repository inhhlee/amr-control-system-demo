# 과제3 GUI 개선본 최신 develop 통합 — Issue #48

## 목적·완료 범위

관제 담당자가 등록 장비·위치/목적지·알람·명령 결과·이력을 이해하고, 선택한 Mock AMR을 별도 창에서 시험할 수 있도록 한 개선본을 현재 develop에 통합한다. 이 기록은 기존 피드백 범위의 반영·보존과 이번 리뷰 수정의 검증 근거다. 과제3 전체·실제 AMR 명령/응답·DB 완료를 뜻하지 않는다.

기존 GUI head `174a050f984014cb32fe35a481646ad1c885b8ac`와 develop `3752d57ef9a6efb05e746763e4393858d0f3bd04`를 병합했다. README 안내 한 곳의 충돌은 GUI 안내와 통신/증거 색인을 모두 유지해 해결했다. 이번 추가 변경은 아래 표시 오류·GUI 시험 격리·문서 연결이다. 검증한 소스/바이너리 SHA256과 보호 범위는 [검사 결과](verification.json)에 기록하고, 실제 게시/병합 상태는 [Issue #48](https://github.com/inhhlee/amr-control-system-demo/issues/48)에서 확인한다.

적용 기준은 #48에 기록된 사용자 피드백·현재 코드·기존 GUI 실행 근거다. 실제 읽은 로컬 기능명세v0.13·결정사항v0.16과 Issue가 인용한 과제3v0.16·기능v0.14는 구분한다. 로컬 과제3 계획의 기존 충돌 표시는 이번 작업에서 임의 해결하지 않았다.

## 개선 내용과 필요한 이유

| 내용 | 목적·결과 | 근거 |
| --- | --- | --- |
| 대시보드·지도·알람·이력 | 등록 수/오류/경고와 출발·목적지·거리·비고를 같은 선택 장비 기준으로 확인 | [기존 피드백 구현](../20261030-figma-feedback-01/README.md) |
| 별도 Mock 실행창·출발지 설정·시작/Stop | 화면 조회와 시험 조작을 구분하고, 선택 변경이 이미 열린 창의 대상에 영향을 주지 않도록 함 | [출발지 설정](../20261030-source-settings-01/README.md) · [기존 실제 시작/Stop](../20261030-s02-start-stop-01/README.md) |
| 이력 초기화 | 조회 조건·선택을 초기화하며 원본 기록을 지우지 않음 | FeedbackViewTests·기존 화면 기록 |
| 중단된 MOVE 응답 표시 수정 | 이동 중 창 닫힘→재열기 때 실패한 MOVE를 `Stop · 정지 완료`로 표시하던 오류를 `이동 중단 · 미완료`와 실패색으로 표시. 실제 STOP 성공과 구분 | [수정 전 재현](window-close-red.log), 최종 GUI65에 같은 재현 포함 |
| GUI 시험 격리 | 같은 데스크톱의 창/모달 시험을 겹치지 않게 실행. 모달 창이 표시된 뒤 조작하며 기존 제한 시간·검증 조건과 시험 내부 동시성은 유지 | GuiTestAssembly·FeedbackViewTests, 최종 [GUI65](gui-serial.trx) |
| 안내·링크 | 기존 develop 기능/증거 정리를 유지하며 최신 GUI 근거를 색인에 연결. 기존 출발지 README의 상위 경로 오류3개 수정 | 루트 README·BUILDING·evidence 색인·verification |

## 이번 검증

Windows / .NET SDK10.0.401, Debug, 기존 패키지 버전. 테스트의 Windows 폼/testhost는 허용된 샌드박스 밖 환경에서 실행했다. GUI 시험은 실제 폼/컨트롤을 자동 구동하며 이번에 사람의 키보드·마우스 조작/새 화면 캡처를 수행한 것은 아니다.

| 입력·절차 | 예상 | 실제·판정 |
| --- | --- | --- |
| 전체 솔루션 최종 빌드 | 기존 실행 경로와 공유 계약이 모두 컴파일 | [build-final.log](build-final.log), 경고0·오류0 통과 |
| GUI65 | 출발지 적용/취소·시작/Stop·이력 초기화·대상 고정·화면 배치·닫기/재열기·기존 격자/Mock | [gui-serial.trx](gui-serial.trx) · [로그](gui-serial.log), 65/65 통과 |
| Core9 | 변경된 공유 데이터와 기존 장비 도메인 호환 | [core.trx](core.trx), 9/9 통과 |
| Map72 | 기존 격자·조회/통신 어댑터 회귀 | [map.trx](map.trx), 72/72 통과 |
| 보존·링크 | 통신/Simulator/지도/설정의 최신 develop 유지, 정리된56개 재등장 없음, 사용자35개 유지, 새 누락 없음 | [verification.json](verification.json) |

이번 최종 자동시험은 **146건**이다. 통신826·Simulator244는 이번에 다시 실행하지 않았고 해당 제품/시험 소스가 develop과 동일한 근거로 기존 결과를 유지한다. 과거 GUI64·전체1152와 이번146을 새 실행 총수처럼 합산하지 않는다. 기존10/30 실제 화면 기록은 그 당시 실행이며 표시 수정 전 버전과 구분한다.

재현 명령:

```powershell
dotnet build AmrControlSystem.slnx -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/Amr.ControlGui.Tests/Amr.ControlGui.Tests.csproj -c Debug --no-build --no-restore
dotnet test tests/Amr.Core.Tests/Amr.Core.Tests.csproj -c Debug --no-build --no-restore
dotnet test tests/Amr.Simulator.Map.Tests/Amr.Simulator.Map.Tests.csproj -c Debug --no-build --no-restore
```

## 실패·진단·수정 이력

- 표시 오류는 [window-close-red.trx](window-close-red.trx)의1건 실패로 재현했다. MOVE 결과는 Failure인데 화면은 Stop 완료였다. 검증 조건을 바꾸지 않고 표시 분기를 수정했으며 최종65에 회귀시험을 포함했다.
- 최초 GUI 전체는 [gui.trx](gui.trx)에서64/65 통과·설정창1건15초 초과였다. 제한 시간을 늘리지 않았다.
- 단독 설정창 진단은 [source-dialog-probe.trx](source-dialog-probe.trx)의1건, 같은 화면 묶음은 [feedback-class-probe.trx](feedback-class-probe.trx)의4건 통과였다. 모달 표시 시점 동기화만 적용한 [gui-final2.trx](gui-final2.trx)에서도 전체 병렬 실행의 같은 대기 초과가 남았다. GUI 시험간 병렬 실행을 격리한 최종65가 통과했다. 최초 초과의 OS 내부 원인은 확정하지 않으며 관측한 실행 조건과 결과를 구분한다.
- 시험 보조 코드의 변수 이름 중복 컴파일 오류는 [gui-final.log](gui-final.log)에 보존하고 이름을 수정했다. 제품 기능 실패나 자동시험 통과로 집계하지 않는다.
- 진단 중의 임시 단계 로그는 최종 시험 소스에서 제거했다. 각 재실행은 새 파일명을 사용해 실패 원본을 덮어쓰지 않았다.

## 설계·리뷰 참고와 한계

조회/명령 계약을 유지하고 상태·이동 허용은 Mock 공급자가 처리하며 폼은 표시·창 수명을 맡는다. 자식 창 닫힘은 그 Mock AMR의 이동만 중단하고 공유 공급자/다른 장비는 유지한다. MOVE 접수·도착·중단과 별도 STOP 성공을 구분한다. 시작/Stop은 실제 MQTT Command를 발행하지 않는다.

UI 테스트를 클래스로만 격리하는 대안도 가능하지만 현재 작은 GUI 시험 프로젝트는 데스크톱 창 조작을 순차 실행하는 단순한 방법을 택했다. 테스트 실행 병렬성이 줄어드는 대신 명시적 Task.WhenAll 등 제품 경합 시험은 유지한다. 최종 GUI 시험 자체는 약11초이며 일반적인 성능 우열을 측정한 것은 아니다.

리뷰 질문: 중단이 성공으로 보이는가? 실패 결과를 먼저 판정하고 회귀시험으로 확인한다. 기존 격자/통신을 덮었는가? 해당 코드와 설정의 develop 동일성 및 지도72로 확인한다. GUI64/기존 화면을 이번에 전부 새로 확인했는가? 이번65와 과거 수동 기록을 구분한다. 실제 비동기 I/O 공급자를 실행창에 연결하거나 화면 시험 규모가 커지면 수명/취소·시험 격리 단위를 다시 검토해야 한다.

실제 AMR MOVE/STOP/RESET·Alarm/ACK, DB 영속 이력·운영 배차/안전·전체 과제 검증·문서 원본 충돌 정리는 후속이다. 이번에는 해당 기능을 추가하지 않았다.
