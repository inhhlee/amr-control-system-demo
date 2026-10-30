# GUI-04 출발지 설정 창 누락 보완

- 실행 ID: 20261030-source-settings-01 · 2026-10-30 KST
- 코드: develop `2339df1b1955b26c10f93295fcb3ee2622fc6131` + 기존 Figma 피드백 미커밋 변경 + 이번 수정. 커밋/푸시/PR 없음.
- 환경: Windows, .NET SDK 10.0.401, net10.0-windows, Windows Forms, 기본 Mock 모드.
- 기준: 사용자 첨부 `codex-clipboard-2bb48bec-9f94-4710-a313-2acc29831c35.png`의 출발지 설정 화면. 과제3 계획 v0.16, 기능명세 v0.14 GUI-02/04/05, D-11 조회/통지 계약을 유지한다. Figma MCP 조회는 Starter 호출 한도에 차단되어 해당 파일의 최신 원격 노드를 재확인하지 못했으며, 이번 첨부 이미지를 적용 기준으로 사용했다.

## 문제와 변경

기존 `SimulationForm`은 출발지 선택을 드롭다운으로 구현했으나, 시안의 `설정` 버튼 → 별도 출발지 설정 창 → S-01/S-02 `선택·적용` 흐름을 누락했다.

- [SourceSelectionForm.cs](../../../../src/Amr.ControlGui/SourceSelectionForm.cs): AMR ID, 두 출발지의 좌표, 현재 출발지 강조, 선택·적용/닫기 버튼을 표시한다.
- [SimulationForm.cs](../../../../src/Amr.ControlGui/SimulationForm.cs): `OpenSourceSettings()`에서 모달 창을 열고 OK로 반환된 출발지만 기존 `SetSimulationSource()`에 전달한다. 닫기/Escape/창 닫기는 공급자를 수정하지 않는다. 시작 후 설정 비활성화와 공급자의 Ready 재검사를 유지한다.
- 공급자·이동 엔진·시작/Stop·이력 초기화의 동작은 변경하지 않았다. 설정 창은 공급자를 소유하거나 구독하지 않으며 호출자가 `using`으로 해제한다. 기존 라벨 생성 함수를 재사용했다.
- [BUILDING.md](../../../../BUILDING.md)에 실제 조작 순서와 코드 위치를 보완했다.

## 자동 검증

실행: `dotnet test tests/Amr.ControlGui.Tests/Amr.ControlGui.Tests.csproj -c Debug --no-restore -m:1 /nr:false --logger "trx;LogFileName=source-settings.trx" --results-directory evidence/GUI/20261030-source-settings-01/tests`

GUI 및 참조 프로젝트 빌드 성공. 첫 실행은 샌드박스의 testhost 연결이 90초 후 시간 초과되어 중단됐다. [첫 TRX](tests/source-settings.trx)를 보존했다. 같은 바이너리를 샌드박스 밖에서 `--no-build`로 재실행하여 **64건 통과, 실패/건너뜀 0**을 확인했다. [최종 TRX](tests/source-settings-final.trx).

| 입력 | 예상 및 실제 결과 | 판정 |
| --- | --- | --- |
| 설정 창 열기 → 닫기 또는 창 닫기 | 기존 AMR 상태·좌표·이력 동일 | 통과 |
| S-02 선택·적용 | AMR-01 출발지 S-02, 현재 좌표 (4,2), 대기 유지, 창 좌표 갱신, 설정 이력 1건 추가 | 통과 |
| AMR-01 설정 변경 | AMR-02 상태·좌표 유지 | 통과 |
| 설정 창의 선택·적용/닫기/안내 영역 | 부모 영역 내 표시, 잘림 없음 | 통과 |
| S-02 적용 → 시작 → 2초 이동 → Stop | 이동 중 설정 비활성, Stop 후 정지, 대상 AMR 고정 | 통과 |
| 이력 조회/선택 → 초기화 | 조건·선택 해제, 원본 기록 유지 | 통과 |

## 실제 앱 확인

18:30~18:32 KST, 최신 Debug EXE를 실행하여 다음을 직접 확인했다.

1. AMR-01 별도 실행창에서 설정 클릭 → 두 출발지 좌표와 선택·적용/닫기 버튼 표시. [설정 창](screens/01-source-dialog-1.png), [실행창과 설정 창](screens/01-source-dialog-0.png).
2. S-02 선택·적용 → 설정 창 닫힘, 실행창 출발지 S-02 / X4.0m·Y2.0m / 대기 표시. [적용 결과](screens/02-source-applied.png).
3. 관제 지도에 출발지 S-02와 AMR-01 (4,2), 출발–목적지 거리21m 표시. 이력은 알람2건에 설정1건을 더한3건. [관제 화면](screens/04-map-source-visible.png), [접근성 텍스트](screens/03-map-source-applied.txt).

이번 변경의 시작/Stop·이력 초기화 회귀는 위 자동 시험으로 확인했다. 해당 동작의 이전 실제 화면 검증은 [기존 기록](../20261030-figma-feedback-01/README.md)이며, 이번 수동 조작으로 다시 실행한 것으로 표시하지 않는다. 실제 MQTT/DB·다익스트라 경로 탐색은 이번 범위가 아니다.

## 선택 이유와 검토 참고

- 모달 설정 창은 해당 실행창에서 설정 중 시작을 동시에 누르지 못하게 하며, 적용/취소 의도를 구분한다. 기존 드롭다운은 조작 수가 적지만 사용자 시안의 별도 선택·적용 흐름과 맞지 않았다.
- 창은 선택값만 반환한다. 기존 공급자가 상태 검사·좌표 변경·이력·통지를 수행하므로 두 개의 상태 원본을 만들지 않는다.
- 예상 질문: 취소해도 값이 바뀌는가? 변경하지 않는다(자동 시험). 이동 중 바꿀 수 있는가? 버튼 비활성과 공급자 검사로 거부한다. 다른 AMR에 영향이 있는가? 실행창 생성 시 고정된 ID만 변경하며 자동 시험으로 확인했다.
- 검증 범위는 기본 Mock 실행창 보완이다. 전체 과제/실제 AMR 연동 완료를 뜻하지 않는다.
- 문서 저장소의 `C:\process-plan\일일업무\2026-10-30.md`는 존재하지 않음을 확인했다. 해당 저장소는 이번 세션의 쓰기 허용 범위 밖이므로 새 일일업무 문서를 생성하지 않았다. 이 기록은 구현·실행 증거이며 과제계획·업무 기록을 대체하지 않는다.
