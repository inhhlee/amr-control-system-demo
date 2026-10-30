# S-02 설정 → 시작 → Stop 실제 화면 재검증

- 실행 ID: 20261030-s02-start-stop-01 · 2026-10-30 18:34~18:38 KST
- 코드: develop `2339df1b1955b26c10f93295fcb3ee2622fc6131` + 출발지 설정 창을 포함한 기존 미커밋 변경. 이번 요청에서는 제품 소스를 수정하지 않았다.
- 환경: Windows / .NET SDK 10.0.401 / Debug net10.0-windows / WinForms 기본 Mock 모드. Broker·실제 AMR·DB 미사용.
- 실행 파일: `src/Amr.ControlGui/bin/Debug/net10.0-windows/Amr.ControlGui.exe`
- 바이너리: `Amr.ControlGui.dll` SHA256 `1D12A8B83C04FDF85DF1A4D843C7C1195E073A77C40F78BD8B7291FCC9F748DC`. [직전 64건 자동 시험 버전](../20261030-source-settings-01/code-hashes.json)과 일치한다. 이번에는 자동 시험을 반복하지 않고 실제 화면을 조작했다.

## 빌드와 재실행

기존 앱을 정상 종료하고 `dotnet build src/Amr.ControlGui/Amr.ControlGui.csproj -c Debug --no-restore -m:1 /nr:false` 실행: 성공, 경고0·오류0. 해당 경로의 EXE를 새로 실행하여 초기 S-01 (2,2)·이력2건을 확인했다.

## 실제 입력·예상·결과

| 단계 | 실제 입력 | 예상 | 실제 결과·증거 | 판정 |
| --- | --- | --- | --- | --- |
| 출발지 설정 | AMR-01 별도 실행창 → 설정 → S-02 선택·적용 | 출발지 (4,2), 대기 | S-02 / X4.0m·Y2.0m 확인. [설정 창](screens/01-source-dialog.png), [적용 화면](screens/02-s02-applied.png) | 통과 |
| 최종 시작 | 새 세션에서 S-02 재적용 후 18:37:27 시작 클릭 | 이동, 수락/실행 중 응답, Stop 활성 | ‘이동 중’, ‘요청 수락 · 실행 중’. [시작 화면](screens/05-final-start.png) | 통과 |
| 최종 Stop | 18:37:35 이동 중 Stop 클릭 | 도착 전 정지, 정지 완료 응답 | ‘정지’, ‘Stop · 정지 완료’, 시작/Stop/설정 비활성. [Stop 화면](screens/06-final-stop.png), [접근성 텍스트](screens/06-final-stop.txt) | 통과 |
| 이동 위치 확인 | 관제 화면에서 AMR-01 좌표 확인 | 출발지에서 이동한 위치, 목적지 도착 전 정지 | 현재 (15.99,2)m, 이동11.99m, 남은9.01m. [지도 화면](screens/07-stop-map.png), [텍스트](screens/07-stop-map.txt) | 통과 |
| 위치 유지 | 18:38:02와 18:38:20 관제 상태 비교 | 같은 정지 좌표·거리 유지 | 두 조회 모두 (15.99,2)m / 이동11.99m / 남은9.01m, 상태 문구 동일. [관찰 시각·대조 JSON](observations.json), [두 번째 텍스트](screens/08-stop-position-held.txt) | 통과 |

첫 시도에서는 18:35:57 시작 후 다른 창의 접근성 정보를 읽는 과정에서 응답이 비어 재확인하는 동안 18:36:11 목적지에 도착했다. 이는 Stop 통과로 집계하지 않았다. [첫 시작 화면](screens/03-started.png), [도착 화면](screens/04-first-run-arrived.png)을 보존했다. 앱을 다시 실행하고 같은 출발지를 설정한 뒤 위 최종 시작/Stop 순서로 검증했다.

이력에는 출발지 설정, 시작 수락, Stop 수락, 이동 정지 기록이 남았으며 알람2건 포함 보관6건이었다. 실제 MQTT Stop·물리 안전 정지·DB 보존·이력 초기화는 이번 수동 시험 대상이 아니다. 관련 자동 회귀와 앞선 화면 시험은 [설정 창 보완 기록](../20261030-source-settings-01/README.md) 및 [기존 Figma 피드백 기록](../20261030-figma-feedback-01/README.md)을 따른다.

`C:\process-plan` 일일업무 문서는 이번 세션의 쓰기 허용 범위 밖이므로 갱신하지 않았다. 이 문서는 구현·실행 증거이며 업무 기록을 대체하지 않는다.
