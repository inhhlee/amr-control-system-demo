# trial 로그·명령 입력줄 보호 검증

## 1. 배경·목적·완료 범위

사용자가 GUI·가상 AMR·trial을 함께 실행할 때 주기 수신 로그가 명령 입력을 덮는 문제를 보고했다. 수신 로그를 계속 보면서 **같은 창의 마지막 `trial>` 줄에서 명령을 입력·수정·실행**하도록 보완했다.

- 결과물: trial의 입력 문장/커서 보존과 로그 뒤 재표시, 입력 편집, 기존 수신·발행·취소·종료와 파일/파이프 출력 유지.
- 이번 범위: #58 연동 시 발견한 콘솔 표시 결함의 로컬 수정. GUI·Simulator·MQTT 계약·명령 처리 기준·Seq 설정은 변경하지 않았다. 커밋·푸시·PR은 하지 않았다.
- 기준: 과제1 계획의 `trial-input-20261101` 후속, 기존 COM-06/COM-05·기능명세v0.13·공통데이터v0.12의 채택 계약과 2026-11-01 보완. 담당 원본은 `C:\process-plan`이다. 새 입력 키는 사용자 요청을 구현한 화면 조작이며 메시지 요구를 바꾸지 않는다.
- 코드 버전: `feature/58-gui-mqtt-live`, HEAD `755e1405dd4bb74b54a80a97abdc47d244fcfd99` + 미커밋 변경. 기존 GUI 변경/사용자 증거를 보존했다. 파일별 버전·DLL/입력·실제 판정은 [검증 목록](verification.json)에 연결한다.

## 2. 진행 내용과 필요한 이유

| 변경 | 필요한 이유·처리 |
| --- | --- |
| `TrialConsoleDisplay` 입력줄 표시 | 새 로그를 쓰기 전에 입력줄을 지우고, 로그 뒤에 작성 중인 문장과 커서를 다시 표시한다. 로그와 키 표시가 같은 잠금을 사용한다. |
| `TrialConsoleInput`·줄 편집 상태 | 실제 입력은 별도 상태로 보존한다. ←/→·Home/End·Backspace/Delete·Esc를 처리하고 Enter에서만 한 줄을 전달한다. 화면 폭에 맞춰 일부를 보여도 발행할 문자열은 자르지 않는다. |
| `TrialConsoleRunner` 연결 | 실제 대화형 표준 입력/출력일 때 보호된 입력줄을 사용한다. 파일·파이프 또는 주입한 시험 입력은 일반 텍스트 경로를 유지한다. |
| 시험 도구 | 현재 사용자 실행을 종료하지 않고 별도 빌드 경로/격리 Broker로 확인했다. 기존 MQTT 시험에 실행 파일 선택 옵션을 추가해 새 바이너리를 직접 검증했다. |

입력은 기존처럼 ASCII128자까지다. 초과·비ASCII 입력은 잘라 발행하지 않고 거부하며, Esc로 비워 새로 입력할 수 있다. CR/공백/Backspace만으로 프롬프트를 다시 그린다. 터미널 모드·ANSI 커서 제어·별도 화면 버퍼·추가 패키지는 도입하지 않았다. 기존 로그 색상/안전한 메시지 표시·QoS0/retain=false·자동 재전송 금지와 입력 취소 책임은 유지한다.

## 3. 환경·입력·확인 방법

- 실행 ID `20261101-trial-input-01`, 최종 대화형 실행 `live-final`.
- Windows10.0.26200, .NET SDK10.0.401/런타임10.0.12, MQTTnet5.2.0.1603, Mosquitto2.1.2-alpine. 실제 대화형 환경은 [환경 기록](live-final/native-environment.json)에 남겼다.
- 사용자 실행의 파일 잠금을 피하려고 `--artifacts-path tests/Amr.Communication.Tests/bin/trial-input`에 빌드했다. 최종 관리 DLL·소스 해시는 검증 목록을 따른다. 일반 사용자는 기존 trial을 Ctrl+C로 종료한 뒤 [일반 실행 명령](../../../tests/manual/COM06/README.md#로그가-나오는-동안-같은-창에서-명령-입력)을 다시 실행한다.
- 대화형 시험은 도구 전용 Windows ConPTY의 실제 `Console.ReadKey` 경로다. 입력/출력 리다이렉트가 모두 false인 것을 확인했다. VS Code UI 키 주입은 사용자 입력 충돌로 중단했고 별도 cmd UI 열기도 도구 정책으로 차단되어, 직접 VS Code 키 조작 완료로 표시하지 않는다.
- 실제 Broker18887과 독립 명령 대기 AMR(초기-1,0·속도0.1m/s·갱신50ms·Telemetry200ms·Heartbeat1000ms)을 사용했다. [설정](live-final/simulator.json), [소유 자원](live-final/resources.json). 기존 사용자1884 Broker·GUI·AMR·Seq를 중단/변경하지 않았다.

```powershell
dotnet test tests/Amr.Communication.Tests --artifacts-path tests/Amr.Communication.Tests/bin/trial-input --no-restore
pwsh -NoProfile -File tests/manual/COM06/Verify-TrialConsole.ps1 -RunId <새ID> -FinalAcceptance -ConsolePath tests/Amr.Communication.Tests/bin/trial-input/bin/Amr.Communication.Console/debug/Amr.Communication.Console.exe
```

실제 대화형 입력의 순서와 터미널 응답은 [입력·출력 기록](live-final/input-output.json)에 보존했다. 이 기록은 터미널 전송 계층의 화면 제어 시퀀스를 포함한 원본 발췌이며 일반 텍스트 로그 파일과 구분한다.

## 4. 실제 결과

| 구분 | 입력 → 예상 | 실제 결과·근거 |
| --- | --- | --- |
| 자동 회귀 | 기존 통신/콘솔 + 로그 중 편집·동시 출력·좁은 폭·128자 제한·일반 출력 | [833/833](communication-final-03.trx). 새6건 포함, 기존827건 유지. |
| 최종 콘솔 빌드 | 변경된 콘솔 프로젝트 → 경고/오류 없음 | [경고0·오류0](build-final.log). 전체 GUI 솔루션 재빌드로 확대하지 않는다. |
| 실제 파일/파이프 MQTT | 기존 송수신·잘못된 메시지·두 장비/역순 ACK·끊김/재접속·종료 | [20/20](../20261101-trial-input-redirected-01/verification.json). 새 콘솔 실행 파일을 명시했다. |
| 대화형 부분 입력 | `move 100 2`를 Enter 없이 입력하고 로그 수신 → 입력 보존/발행 없음 | 실제 수신 중 `trial> move 100 2` 유지. 첫 발행 시각은 부분 입력 관측 뒤의 Enter 시점이다. |
| 대화형 수정·발행 | Left×2·Backspace·End·Enter → `move 10 2`만 발행 | 실제 Broker에 MOVE(10,2) 1회, 다른 목표100으로 발행 없음. |
| 대화형 STOP | `stoo`를 로그 중 유지한 뒤 Backspace·p·Enter | STOP 1회, 원 MOVE FAILURE/STOP_INTERRUPTED, 별도 STOP SUCCESS/STOPPED, 좌표/속도0 유지. |
| 대화형 재출발·도착 | 새 `move -1 0` → 새 ID/도착 ACK | 새 MOVE 1회, 최종(-1,0)·IDLE·속도0·SUCCESS/ARRIVED. |
| 대화형 종료 | `/exit` → 추가 Enter 없이 종료 | [최종 앱 종료0](live-final/native-result.json), 터미널 소유 작업 종료. |
| 입력 중 단절·취소 | 부분 입력 `st` 상태에서 Broker 중단 → 입력 보존, Ctrl+C 종료 | [재접속/취소 출력](live-input/pty-cancel-01/input-output.json), [앱 종료0](live-input/pty-cancel-01/native-result.json). 바깥 셸의 Ctrl+C 종료코드1과 앱의 종료코드0을 구분한다. |

최종 대화형 판정은 [검증 목록](verification.json)에 8항목으로 연결한다. 실제 명령/ACK·좌표는 [Broker 관찰](live-final/mqtt.log), [AMR 처리 로그](live-final/simulator.log)에 대조했다. Native 첫 실행의 Esc·부분 입력/편집은 [이전 대화형 결과](live-input/pty-01/input-output.json)에 보존했다. 리다이렉트20항목과 취소 결과는 들여쓰기 정리 전의 동일 동작 코드에서 확인했으며, 정리 후 최종833건·대화형 이동/정지/도착/exit와 연결한다.

## 5. 발견 사항·수정·재확인

- 화면의 입력과 로그가 각각 Console에 기록돼 끼어드는 것이 원인이었다. 입력 상태/커서와 출력 잠금을 연결해 해결했다. 새 로그가 입력 데이터를 다시 만들거나 명령을 자동 발행하지 않는다.
- [첫15건](trial-input-first.trx)은 통과했으나 새 시험의 xUnit 분석기 경고1건이 있어 권장 Assert 호출로 고쳤다. 이후 빌드 경고0.
- [중간 빌드](communication-build-test-final.log)는 앞서 만든 숨겨진 시험용 콘솔이 apphost를 잠가 MSB3027/MSB3021로 실패했다. 프로세스 경로·ID·생성 시각을 확인해 시험 소유 프로세스만 정리했고, [최종 빌드/833건](communication-build-test-final-03.log)이 통과했다. 빌드 실패를 제품 입력 처리 실패로 분류하지 않는다.
- 마지막 대화형 실행은 정리 후 최종 소스/DLL로 재확인했다. [이전833건](communication-final.trx)은 중간 실행이며 최종 표에 합산하지 않았다.
- [자원 정리 결과](owned-containers-after.log): 시험용 Broker 제거, 사용자1884 Broker 유지. 마지막 AMR/관찰 프로세스는 시험 도구가 강제 정리했으며 AMR Ctrl+C 정상 종료 검증으로 세지 않는다.

## 6. 설계 판단·남은 범위

**선택 이유:** 입력128자와 커서만 보관하고 원래 로그는 즉시 출력한다. 단일 출력 잠금으로 글자가 섞이지 않게 하며 터미널 전체 화면/로그를 메모리에 쌓지 않는다. 실제 줄 편집과 화면의 수평 표시 범위를 구분해 좁은 창에서도 명령 내용이 바뀌지 않는다.

**대안:** 고정 화면 영역을 사용하는 TUI는 화면 배치 자유도가 높지만 터미널 기능/크기 변경·스크롤 이력 처리가 더 필요하다. 별도 명령 창은 기존 기능으로 가능하지만 같은 창에서 입력하려는 요구를 충족하지 못한다. Seq에서 상세 로그를 보면 검색·분석에 유리하지만 trial의 입력줄 보호는 별도로 필요하다. 대안의 성능 우열을 측정한 것은 아니다.

검토 질문과 근거:

1. **로그가 와도 왜 입력이 유지되는가?** 실제 문장/커서는 편집 상태가 소유한다. 출력 잠금 안에서 로그를 쓴 뒤 동일한 상태를 다시 그린다. 동시 출력 자동시험과 실제 부분 입력 시험으로 확인했다.
2. **명령이 두 번 보내지거나 줄어들지 않는가?** 화면 일부 표시와 입력 데이터를 구분하고 Enter에서만 완성 문자열을 반환한다. 실제 Broker에서 수정한 목표(10,2)와 고유 ID의 발행 횟수를 대조했다. 분산 환경의 중복/유실 방지를 보장하는 주장은 아니다.
3. **취소나 파일 출력은 어떻게 되는가?** 기존 취소 토큰/40ms 입력 감시·세션 종료 책임을 유지한다. 실제 Ctrl+C/exit와 리다이렉트 MQTT20항목을 확인했다. 비Windows·MinTTY의 리다이렉트형 터미널·입력 중 창 크기 변경은 별도 수동 검증하지 않았다.

Seq 서비스 기동/설치/전송 설정 변경·실제 Seq 유입 재시험은 이번 범위에 없다. 기존 연동을 유지한다. GUI의 외부 trial 명령 추적, RESET·장애 시나리오·DB 이력, 과제 전체 완료로 확대하지 않는다. 문서 공식 HTML은 기존 과제1 양식 불일치로 생성되지 않으며 원본 Markdown의 후속 기록을 따른다.
