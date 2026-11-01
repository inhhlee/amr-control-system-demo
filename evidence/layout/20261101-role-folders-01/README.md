# C# 파일의 역할별 폴더 재배치 검증

## 1. 목적과 적용 기준

사용자는 폴더 설명만 추가하는 것이 아니라 **실제 C# 파일을 기능에 맞는 폴더로 묶기**를 요청했다. 기능 수정·시험·검토 시 관련 파일을 함께 찾기 쉽게 하는 것이 목적이다.

- 작업: `C:\amr-control-system-demo`, `feature/58-gui-mqtt-live`, HEAD `755e1405dd4bb74b54a80a97abdc47d244fcfd99` + 기존 미커밋 작업과 이번 이동.
- 기준: 사용자 정정 요청, AGENTS, 원본 amr-implement-verify/record-work 스킬. 기능명세v0.13·공통규칙v0.12·공통데이터v0.12·결정사항v0.16의 기능·통신 계약을 유지한다.
- 원본 기록: `C:\process-plan`의 과제1 계획·개발환경·2026-11-01 일일업무 `role-folders-20261101` 항목. 공통 구조 정리이며 새 기능이나 과제 전체 완료 판정은 아니다.
- 완료조건: 역할별 실제 파일 이동, C#·리소스 내용 보존, 기존 프로젝트/진입점·참조·WinForms 리소스 유지, 전체 빌드·기존 자동시험·경로/링크 확인.

## 2. 변경 범위와 필요한 이유

| 프로젝트 | 현재 폴더 | 이동 수 |
| --- | --- | --- |
| Communication.Console | Modes 기존 실행 모드 · Trial 통합 시험/입력 · 기존 Logging 유지 | C#7 |
| Core | Contracts 인터페이스 · Models 데이터 · Commands 명령 추적 · Domain 관제 판단 | C#11 |
| Infrastructure | Mqtt 실제 통신 공급자 | C#2 |
| Simulator | Execution 실행 조립. 기존 Configuration/Simulation/Navigation/Telemetry 유지 | C#2 |
| ControlGui | Configuration 옵션 · Providers 공급자 · Views/Dashboard·Map·Simulation 화면 | C#15·resx1 |
| 지도 웹 호스트 | Mqtt 수신·좌표 투영. 기존 Control/wwwroot 유지 | C#2 |
| Core/Simulator/GUI/Map 자동시험 | 소스 역할에 대응하는 Commands·Domain·Configuration·Execution·Simulation·Navigation·Mqtt·Providers·Dashboard·Projection | C#30 |

총 **C#69개·resx1개**. [파일별 이전/현재 경로·내용 해시](moves.json) · [현재 소스 트리](../../../src/README.md#source-tree) · [시험 폴더](../../../tests/README.md).

`Program.cs` 진입점, 프로젝트 정의·참조, namespace·형식 이름·동작 코드는 유지했다. MainForm의 partial·Designer·resx는 같은 폴더로 이동했다. 기존 Communication의 기능별 폴더, 공통 시험 대역, 전체 GUI 시험 실행 설정도 유지했다.

현재 README·BUILDING의 코드 링크를 수정했다. 과거 evidence README4개의 링크9개는 현재 파일로 연결하되, 링크 주소를 되돌린 내용 해시가 작업 전 원본과 같음을 확인했다. 원시 실행 로그·TRX·입력·과거 코드/해시 목록은 변경하지 않았다.

현재 실행에 쓰는 COM03-C 시험 도구3개의 소스 수집은 `git ls-files --cached --others --exclude-standard`에서 실제 존재 파일만 수집하도록 보완했다. 미스테이징 이동 중에도 사라진 경로를 해시하지 않고 새 파일을 기록하기 위해서다. 과거 HEAD·ZIP·스냅샷을 전제로 하는 Audit-Heartbeat는 당시 검증 도구로 보존하며 현재 상태 재검증에 실행하지 않았다.

## 3. 환경·입력·절차

[실제 환경·코드 기준](environment.json). .NET SDK10.0.401, Windows, 프로젝트의 기존 패키지를 사용했다. 실행 중인 사용자 프로그램의 빌드 출력과 구분하려고 `tests/Amr.Communication.Tests/bin/role-folders`에 빌드했다. 외부 Broker·Seq를 시작하거나 중단하지 않았다.

PowerShell, 저장소 루트에서 실행한 최종 빌드:

```powershell
dotnet build AmrControlSystem.slnx --artifacts-path tests/Amr.Communication.Tests/bin/role-folders --disable-build-servers -m:1 --verbosity normal
```

각 자동시험 프로젝트에 대해 실행했다. 아래는 통신 시험의 재현 예다. 기존 결과를 덮어쓰지 않도록 재실행 시 새 결과 폴더를 사용한다.

```powershell
dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --no-restore --artifacts-path tests/Amr.Communication.Tests/bin/role-folders -m:1 --logger trx --results-directory evidence/layout/<새-실행ID>
```

이동 전 파일 해시와 현재 파일을 대조하고, 기존 위치가 남지 않았는지 확인했다. GUI 어셈블리의 리소스 이름 조회, 콘솔/Simulator `--help`, PowerShell3개 구문 분석·소스 수집, 현재 문서 링크도 검사했다.

## 4. 실제 결과

| 확인 항목 | 예상 | 실제 판정·증거 |
| --- | --- | --- |
| 파일 이동·내용 | 이전 위치 없음, 새 위치 내용 동일 | 70/70 통과, [moves](moves.json) · [종합 판정](verification.json) |
| 기존 변경 보존 | 이동을 반영한 기존 내용 유지 | 기존3969개 중3956개 내용 동일. 나머지13개는 README/BUILDING10개·시험 도구3개이며 [수정 목록](verification.json)에 명시 |
| 솔루션 빌드 | 경고0·오류0 | 통과, [최종 빌드](build-final.log) |
| 통신·trial·로그 자동시험 | 기존 전체 성공 | [839/839](Amr.Communication.Tests.trx) |
| Core 자동시험 | 기존 전체 성공 | [16/16](Amr.Core.Tests.trx) |
| Simulator 자동시험 | 기존 전체 성공 | [250/250](Amr.Simulator.Tests.trx) |
| 지도 호스트 자동시험 | 기존 전체 성공 | [72/72](Amr.Simulator.Map.Tests.trx) |
| GUI·공급자 자동시험 | 기존 전체 성공 | [84/84](Amr.ControlGui.Tests.trx) |
| WinForms 리소스 | 기존 이름 유지 | `Amr.ControlGui.MainForm.resources` 유지, [리소스 이름](resource-names.log) |
| CLI 진입점 | 기존 옵션 해석 유지 | 두 프로그램 `--help` 종료0, [판정](entrypoint-results.json) · [콘솔](console-help.log) · [Simulator](simulator-help.log) |
| 시험 도구3개 | 구문 정상, 새 파일 포함/삭제 경로 제외 | [구문·소스 수집 확인](script-verification.json) |
| 현재 경로·링크 | 대상/본문 항목 존재 | 240개 확인, [종합 판정](verification.json). 과거 README의 이동 코드 링크9개 포함 |

자동시험 총 **1261/1261**. [현재 C#/프로젝트/리소스 해시](source-hashes.json)로 실제 시험 코드와 연결한다. 이 값은 PDB 경로·재빌드 바이너리까지 동일하다는 뜻은 아니다.

## 5. 실패와 검증 한계

- [첫 빌드](build.log)는 상세 오류 없이 실패했고 경고/오류 수 모두0으로 출력됐다. 원인 미확정이다. 이후 제한 밖에서 빌드 서버를 끄고 단일 MSBuild 노드로 실행한 최종 빌드는 경고0/오류0이었다. 권한 문제로 확정하지 않는다.
- 테스트 기대값·동시성·시간 제한을 변경하지 않았다. 5개 프로젝트를 차례로 실행했고 실패·건너뜀0이다.
- 실제 MQTT·Seq·GUI 통합 시나리오는 이번에 재실행하지 않았다. 이동 파일이 바이트 단위로 같고, 프로젝트 참조·통신 계약·진입점이 유지되며 기존 자동시험 전체가 통과한 범위다. 앞선 실제 연동 근거는 [색인](../../README.md)에 연결되어 있다.
- 계획 HTML은 [첫 시도](document-render.log)에서 marked를 찾지 못했다. 기존 번들 경로를 지정한 [재확인](document-render-configured.log)에서는 이전부터 있던 과제1의 절/하위 제목 순서 불일치로 생성 전에 중단됐다. Markdown 원본의 결과는 갱신했고, HTML 갱신은 남아 있다. 폴더 재배치를 새로운 기능·과제 전체 완료로 표시하지 않는다.

## 6. 설계 선택과 남은 단계

프로젝트 안에서 물리적 파일 위치를 역할별로 정리하고 namespace는 유지했다. 기존 GUI/시험·Core 공개 형식의 참조를 바꾸지 않으면서 파일 탐색을 개선하기 위해서다. 대안으로 namespace도 폴더와 맞추면 이름으로 경계를 알기 쉬워지지만, 호출부·디자이너·리소스 참조 변경이 늘어난다. 이번에는 동작/공개 계약 보존을 우선했다. 성능·메모리 효과를 측정한 변경은 아니다.

검토 질문:

1. **namespace가 폴더와 달라도 되는가?** SDK가 파일을 포함하고 기존 namespace로 컴파일한다. 실제 전체 빌드·시험과 리소스 이름으로 확인했다. 향후 공개 API 경계를 바꾸는 작업에서는 namespace 변경을 별도로 검토한다.
2. **MainForm 파일들을 나눠도 디자이너·리소스가 유지되는가?** 모든 partial/Designer/resx를 Views/Dashboard에 함께 배치했고, 리소스 이름과 GUI84건을 확인했다. Visual Studio 디자이너의 수동 열기·편집은 이번에 수행하지 않았다.
3. **과거 근거의 경로는 어떻게 찾는가?** 당시 원시 로그·해시 목록을 유지하고 moves.json의 이전→현재 경로를 사용한다. 현재 README와 과거 README의 코드 링크는 새 위치로 연결했다.

요청한 로컬 파일 재배치·문서 연결·검증 완료. 커밋·푸시·PR·병합과 새 기능 개발은 수행하지 않았다.
