# A3 빌드·자동시험 및 첫 실제 종료 시험

- 실행 ID: 20261011-a3-shutdown-02, 2026-10-11, COM-03 / Issue #8.
- [01의 복원](../20261011-a3-shutdown-01/restore.log)을 사용했다. 프로젝트·패키지 파일 변경 없음.
- [빌드](build.log): 성공, 경고 0·오류 0. [자동시험](tests.log): 6개 통과·실패 0·건너뜀 0. [TRX](tests.trx), [명령과 시각](commands.jsonl), [환경](environment.json).
- [터미널 첫 시도](terminal-first-attempt.json): 실제 Ctrl+C에 앱이 정상 해제·Disposed·ExitCode=0을 출력했으나 외부 PowerShell도 신호를 받아 도구 종료 코드 1이었다. 앱 출력만으로 실제 OS 종료 코드를 판정하지 않았다.
- [별도 네이티브 프로세스 첫 실행](native-1.json): Ctrl+C 전 3초 유지 후 정상 해제·정리, 실제 Process.ExitCode 0.
- 두 번째 실행은 시험 도우미가 자신의 Ctrl+C 무시 플래그를 초기화하지 않아 새 자식에 상속되는 문제가 있었다. 종료 기한 초과로 도우미가 자기 자식을 정리했다. [두 번째 출력](native-2.stdout.log), [실패 정리 기록](native-2-harness-failure.txt), [당시 스크립트](verify-native-console.ps1). 이 실행은 정상 종료 통과로 집계하지 않는다.
- 원인 근거: Windows의 SetConsoleCtrlHandler(NULL, TRUE) 무시 속성은 자식에게 상속된다. [Microsoft 공식 문서](https://learn.microsoft.com/en-us/windows/console/generateconsolectrlevent).
- 도우미가 첫 시험 후 플래그를 초기화하도록 수정하고 새 실행 ID [03](../20261011-a3-shutdown-03/README.md)에서 두 번 재검증했다. 제품 코드와 자동시험 소스는 빌드 통과 이후 변경하지 않았다.
