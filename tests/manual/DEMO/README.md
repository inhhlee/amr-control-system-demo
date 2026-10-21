# 책임님께 보여드리는 보고 순서 — 과제1 → 과제2 → 남은 일

[보고 화면 열기](../../../evidence/DEMO/20261021-report-demo-03/report.html)

보고할 때는 이 화면을 열어 위에서 아래로 설명합니다. 과제1은 2026-10-21에 저장한 통신 시험 결과, 과제2는 사용자가 직접 실행해 이 대화로 전달한 이동 로그입니다. 화면 자체는 실시간 관제 화면이 아닙니다.

| 순서 | 보여줄 부분 | 설명할 말 |
| --- | --- | --- |
| 1 | 과제1 · 저장된 통신 시험 | 정상 정보 20건을 받고 잘못된 정보 4건은 거절했습니다. 명령 3종의 전달과 연결 유지·서버 종료 감지도 확인했습니다. |
| 2 | 과제2 · 사용자 직접 실행 | (-1, 0)에서 출발해 (1, 0)을 지나 (1, 2)에 도착했습니다. 직접 실행한 로그에서 4.064초 뒤 대기 상태와 속도 0으로 바뀐 것을 확인했습니다. |
| 3 | 남은 일 | 다음에는 이동 중인 위치를 관제 쪽으로 보내고, 받은 명령에 따라 움직이도록 연결하겠습니다. 명령 응답 확인·자동 재연결·AMR별 정보 중단 감지도 남아 있습니다. |

명령은 시험용 수신기까지 전달된 것을 확인했습니다. 실제 가상 AMR이 명령대로 움직이는 연결은 다음 작업입니다. 서버를 다시 켠 뒤에는 프로그램을 새로 실행해 연결했습니다.

과제2의 경로 그림 아래 **직접 실행한 로그와 확인 범위**를 펼치면 로그 발췌와 코드·테스트 근거를 볼 수 있습니다. 사용자가 전달한 실행의 종료 코드는 아직 미확인입니다. 질문을 받으면 과제1 상세 시험 근거 또는 전체 결과 문서도 엽니다.

## 저장된 화면을 직접 열 때

탐색기에서 `C:\amr-control-system-demo\evidence\DEMO\20261021-report-demo-03\report.html`을 두 번 클릭합니다. PowerShell 7에서는 아래 한 줄로도 열 수 있습니다.

~~~powershell
Invoke-Item 'C:\amr-control-system-demo\evidence\DEMO\20261021-report-demo-03\report.html'
~~~

## 새 시험 결과로 보고할 때 — 보고 전에 준비

Docker Desktop이 실행 중인 상태에서 PowerShell 7에 아래를 입력합니다. 평소 사용하는 서버와 별도로 시험용 서버를 만들고 정리합니다.

~~~powershell
Set-Location C:\amr-control-system-demo
$demoRunId = 'report-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
pwsh -NoProfile -File tests/manual/DEMO/Invoke-ReportDemo.ps1 -RunId $demoRunId
~~~

마지막에 `PASS: report demo; ...`가 표시된 것을 확인한 다음, 같은 창에서 보고 화면을 만듭니다. 실패한 실행은 성공으로 보고하지 않고 오류와 실행 번호를 남깁니다. 재시험에는 새 실행 번호를 사용합니다.

~~~powershell
pwsh -NoProfile -File tests/manual/DEMO/Write-ReportDemoSummary.ps1 -RunId $demoRunId
Invoke-Item ".\evidence\DEMO\$demoRunId\report.html"
~~~

화면 생성 명령은 저장된 결과를 읽으며 시험을 새로 실행하지 않습니다. 수신·거절·명령 건수는 해당 실행의 결과를 사용합니다. 이번 보고 폴더의 `user-movement.json`과 `user-movement.md`에는 사용자 제공 근거가 있으며 재생성해도 과제2 절이 유지됩니다. 다른 실행 폴더에 이번 사용자 결과를 자동으로 붙이지 않습니다. 시험 도구는 만든 서버·볼륨을 정리하며 기존 서버·설정은 유지합니다. 커밋·푸시는 수행하지 않습니다.

원본 기록은 `evidence/DEMO/실행번호/`와 `evidence/COM-01`, `COM-05`, `COM-03` 아래 같은 실행 번호의 폴더에 있습니다. `ReuseAutomaticFrom` 옵션은 기존 자동시험 재사용용이며 처음에는 기본 명령을 사용합니다.
