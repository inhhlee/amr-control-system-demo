# PR #45 최소 창 명령 결과 잘림 수정 — 20261025-pr45-fix-01

[Issue #44](https://github.com/inhhlee/amr-control-system-demo/issues/44)·[PR #45](https://github.com/inhhlee/amr-control-system-demo/pull/45)의 P2 리뷰 지적을 수정했다. `67c6d2829e256ab3d8ef428a4479cc59cc524734` 위 변경이며 [검증 당시 23파일 SHA256](source-manifest.json)으로 실제 시험 소스를 식별한다. 기준은 과제3 과제계획 v0.11의 GUI-01/04 접점과 현재 Mock 범위다. 기존 [리뷰·실패 재현](../20261025-pr45-review-01/README.md)은 당시 결과로 보존한다.

## 수정과 완료조건

- `MainForm.Designer.cs`: 명령 입력의 고정 96px을 내용에 맞춘 자동 높이로 바꾸고 버튼을 두 줄로 배치했다. `다음 상태` 버튼의 중복 Mock 문구를 줄였으며 영역 제목과 성공/실패 버튼에는 Mock 표시를 유지한다. 결과 표 바깥 여백을 제거해 남은 높이를 표에 배정한다.
- `DashboardMapTests.cs`: 실제 최초 표시가 끝난 뒤 1000×720 → 1200×900 → 1000×720으로 변경한다. 입력·버튼·안내·표의 상위 컨테이너 잘림, 가로 스크롤바, 각 결과 행을 세로 스크롤한 뒤의 가시 높이, 설명 열의 가로 스크롤 접근을 확인한다. 기존 최소 크기 시험도 최초 표시 이벤트를 먼저 처리한다.
- `BUILDING.md`: 버튼 이름과 최소 크기의 스크롤 확인 방법을 갱신했다.
- 최소 크기 1000×720을 유지한다. Core 계약·Mock 처리·화면의 비동기 작업과 종료 책임은 변경하지 않았다.

## 실행 환경·입력·결과

Windows x64, .NET SDK 10.0.401, net10.0-windows, 실제 GUI DPI 96. 저장소 루트에서 아래 명령을 실행했다. 전체 제품 빌드는 경고/오류 0이다. 별도 화면 캡처 도구의 경고는 아래에 구분한다.

| 실행 | 입력·예상 | 실제·판정 | 근거 |
| --- | --- | --- | --- |
| 수정 전 새 회귀 시험 | 원본 GUI + 새 시험, 최소 크기에 결과 두 건. 행과 가로 스크롤바 표시 기대 | 1건 실패. 높이 부족으로 가로 스크롤바도 숨겨짐 | [로그](baseline-test.log), [TRX](baseline.trx) |
| 수정 후 솔루션 빌드 | 기존 전체 솔루션 컴파일 | 성공, 경고0/오류0 | [build.log](build.log) |
| 수정 후 GUI 시험 전체 | 기존 31건 + 새 최소 크기 회귀 1건 | 32건 통과, 실패/건너뜀0 | [로그](gui-tests.log), [TRX](gui.trx) |
| 실제 Mock 폼 자동 조작 | 002 선택 → 다음 상태 → MOVE → 성공 → 최소 크기 → MOVE → 실패 → 확대 → 최소 크기 → 닫기 | 성공1/실패1, 002 X6·BUSY 유지. 최소 크기 표72px = 머리글26 + 가로 스크롤17 + 행 공간29. 필요한 행25px 이상. 정상 닫힘 | [측정](ui-verification.json), [성공 화면](01-minimum-success.png), [실패 화면](02-minimum-failure.png), [확대](03-expanded.png), [재축소](04-minimum-again.png) |

수정 전 리뷰의 행 공간은 8px이었다. 이번에는 모든 입력/버튼이 화면 안에 있고 결과 행이 온전히 보이는 것을 측정과 이미지로 확인했다. 실제 폼 검증은 메시지 루프에서 버튼의 PerformClick을 호출한 자동 조작이며 사람이 마우스로 시험한 것으로 표시하지 않는다.

```powershell
dotnet test tests/Amr.ControlGui.Tests/Amr.ControlGui.Tests.csproj --no-restore --filter FullyQualifiedName~CommandResultsAndControlsFitAfterResizingTheShownWindow --logger "trx;LogFileName=baseline.trx" --results-directory evidence/GUI/20261025-pr45-fix-01 -m:1 -nr:false -p:UseSharedCompilation=false
# 위 baseline은 제품 수정 전 실행 결과다. 현재 코드로 다시 실행하면 통과한다.
dotnet build AmrControlSystem.slnx --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/Amr.ControlGui.Tests/Amr.ControlGui.Tests.csproj --no-build --no-restore --logger "trx;LogFileName=gui.trx" --results-directory evidence/GUI/20261025-pr45-fix-01
dotnet build evidence/GUI/20261025-pr45-fix-01/Capture.csproj -m:1 -nr:false -p:UseSharedCompilation=false
& .\evidence\GUI\20261025-pr45-fix-01\bin\Debug\net10.0-windows\Amr.ControlGui.Tests.exe C:\amr-control-system-demo\evidence\GUI\20261025-pr45-fix-01
```

재실행할 때에는 기존 결과를 덮어쓰지 않도록 별도 결과 디렉터리를 사용한다.

## 이전 실행·경고·미검증 구분

- 새 시험 작성 직후 최초 시험 프로젝트 빌드는 nullable 3건과 xUnit 사용 1건의 경고가 있었다([baseline-build.log](baseline-build.log)). 해당 시험 코드를 보완한 다음 수정 전 실패를 확인했고, 최종 솔루션 빌드는 경고0이다.
- 별도 화면 캡처 프로젝트는 생성된 ApplicationConfiguration과 참조 GUI의 동일 이름 때문에 CS0436 경고1/오류0이었다([capture-build.log](capture-build.log)). 캡처 프로젝트의 초기화가 선택됐으며 제품/시험 솔루션 경고와 구분한다. 제품 프로젝트에 캡처 도구를 추가하지 않았다.
- 기존 `../20261025-dashboard-map-06/`의 통신768·Simulator154는 코드 변경이 없어 재실행하지 않았다. 이번 실행한 GUI32와 기존922건을 합쳐 ‘이번 전체954건 실행’으로 표시하지 않는다.
- 실제 MQTT·DB, 운영 부하, 다른 DPI, Visual Studio 디자이너, 전체 알람·이력 화면은 이번 수정 검증 대상이 아니다. 다른 DPI에서 같은 보장이 필요하면 그 환경의 배치 검증을 추가해야 한다.
- 실패 로그·TRX·이전 리뷰 근거를 보존했다. 검토 근거 파일은 Git에 원시 바이트로 저장하고 빌드 bin/obj는 기존 제외 규칙을 따른다.

## 설계 판단과 PR 질문

명령 입력에 필요한 높이만 사용하면 최소 창 크기와 현재 화면 구성을 유지하면서 별도 스크롤 컨테이너·이벤트 처리 없이 결과 공간을 확보할 수 있다. 최소 창 높이를 올리는 대안은 코드가 더 단순하지만 작은 화면의 사용 가능성을 줄인다. 명령 전체에 스크롤을 추가하면 향후 입력 확장에는 유리하지만 입력과 결과가 번갈아 가려질 수 있고 스크롤 조작이 늘어난다. 이번 배치 변경은 통신 처리량·버퍼·모델 메모리를 바꾸지 않는다. 대안의 성능을 측정한 것은 아니다.

1. **시험은 원래 결함을 잡는가?** 제품 수정 전 동일 새 시험이 실패했고 수정 후 전체 GUI32건에서 통과했다. 최초 표시 이벤트 처리 후 크기를 다시 확인하므로 초기 창 크기를 최소 크기로 오인하지 않는다.
2. **최소 크기에서 두 결과를 동시에 보여야 하는가?** 최소 크기에는 한 행과 스크롤바를 보장한다. 시험은 각 결과로 세로 스크롤해 한 행 전체가 보이는지 확인하며 요청 ID·설명으로 가로 스크롤할 수 있는지도 확인한다.
3. **언제 다른 배치가 필요한가?** 명령 입력·버튼이 추가되거나 지원 DPI에서 결과 공간이 부족해지면 명령 영역을 분리하거나 스크롤 배치를 검토한다. 현재 성공은 DPI96과 검증한 크기 범위에 한정한다.

이번 범위는 P2 수정·검증·같은 PR 반영이다. PR 병합과 Issue 종료는 다음 단계로 유지한다. 문서 저장소의 업무 기록은 이번에 수정하지 않았다.
