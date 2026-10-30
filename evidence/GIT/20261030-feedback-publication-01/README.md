# 과제3 GUI 피드백 변경 게시 검사 — Issue #48

- 기준: `develop`의 `2339df1b1955b26c10f93295fcb3ee2622fc6131`.
- 작업 브랜치: `feature/48-gui-feedback`.
- 사용자 승인 범위: 오늘 GUI 코드·테스트·실행 증거의 커밋·원격 게시. 한글 보고서와 비교 이미지, PR 생성·병합은 제외한다.
- 기존 `--grid-url` 격자/MQTT 모드와 과거 미추적 자료·`.vscode` 설정·보존 브랜치·stash는 유지한다.

## 선별 범위

Core의 조회/통지 데이터와 명령 인터페이스, Mock 공급자, 관제 화면·지도·별도 실행창·출발지 설정 창, GUI 자동시험, README·BUILDING의 실행 안내를 포함한다. 세 실행 증거 폴더의 실패·중간 관찰과 최종 성공 기록도 함께 보존한다.

- [Figma 피드백 구현과 전체 회귀](../../GUI/20261030-figma-feedback-01/README.md)
- [출발지 설정 창 보완과 GUI64건](../../GUI/20261030-source-settings-01/README.md)
- [S-02 → 시작 → Stop 실제 화면](../../GUI/20261030-s02-start-stop-01/README.md)

위 링크의 작성 당시 미커밋 표기는 과거 상태다. 최종 게시 커밋과 원격 확인 결과는 [Issue #48](https://github.com/inhhlee/amr-control-system-demo/issues/48)에 연결한다.

## 이번 게시 전 검사

- Windows / .NET SDK 10.0.401. 전체 11개 프로젝트 Debug 빌드 성공, 경고0·오류0. [빌드 로그](build.log).
- 최신 출발지 설정 검증의 소스3개와 GUI DLL SHA256이 기존 `code-hashes.json`과 일치한다. 재빌드 뒤에도 GUI DLL은 `1D12A8B83C04FDF85DF1A4D843C7C1195E073A77C40F78BD8B7291FCC9F748DC`다.
- 기존 자동시험: 통신805·Simulator202·GUI64건 통과 기록을 재사용한다. 이전 GUI63건과 최종 GUI64건을 합산하지 않는다.
- 빌드에서 Core·Map 시험 출력 폴더의 Core 참조 DLL이 갱신되어 해당 시험만 재실행했다. [Core9건](tests/core.trx), [Map72건](tests/map.trx) 통과, 실패/건너뜀0. 기존 재사용1,071건과 이번81건을 합친 적용 근거는 1,152건이며 전부 이번에 새로 실행한 수치가 아니다.
- 이번에는 제품 소스를 수정하지 않았고 실제 GUI·MQTT 조작을 반복하지 않았다. 출발지 설정·시작/Stop·이력 조건 초기화의 실제 화면 근거는 위 당일 기록을 따른다.
- [검사 목록](audit.json)에 선별 파일, 기존 검증 해시 대조, 제외한 미추적 파일의 보존 확인과 브랜치·stash 참조를 기록한다. 이 파일은 커밋 전 검사 기록이며 원격 게시 성공을 미리 주장하지 않는다.

실제 MQTT Command/ACK·물리 AMR 안전 정지·DB 영속 이력·운영 재시작/교통 제어는 이번 범위가 아니다. `C:\process-plan`의 계획·업무 기록 원본과 Desktop 한글 보고서는 변경하지 않는다.
