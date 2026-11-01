# GUI·통신 시험 개선과 역할별 파일 이동의 분리 게시

## 목적·승인 범위

사용자가 로컬 GUI·Seq·trial 입력·폴더 정리를 커밋/푸시하고, 이미 병합된 보조 Issue #46을 정리하도록 요청했다. 장애 알람 기능을 추가하기 전에 현재 변경을 보존하고, 기능 수정과 대규모 파일 이동을 각각 검토할 수 있게 한다.

- 기존 브랜치: `feature/58-gui-mqtt-live`, 시작 `755e1405dd4bb74b54a80a97abdc47d244fcfd99`.
- 게시 대상 저장소: `inhhlee/amr-control-system-demo`, 후속 PR base는 develop.
- 이번 범위는 커밋·푸시와 #46 정리다. 새 PR 생성·리뷰·develop 병합·#58 종료·#50 구현은 별도다.

## 커밋 구분

| 순서 | 내용 | 확인 방법 |
| --- | --- | --- |
| 1 | 실제 MQTT GUI·MOVE/STOP 일부, trial 입력줄·Seq 로그 개선과 해당 실행 근거 | 기능 커밋 [43c1190](https://github.com/inhhlee/amr-control-system-demo/commit/43c11901ed1c526916bd45eaaee70232a705d0d6), 변경295경로. C# 파일은 이동 전 경로로 기록 |
| 2 | C#69개·resx1개를 역할 폴더로 이동, 코드 링크·소스 수집 도구·README 구조 이미지 정리 | 첫 커밋과 비교해 70개 `R100` 이동을 대조. 이동 파일의 내용 변경 없음 |

작업 폴더의 파일을 되돌리지 않고 임시 Git 인덱스에서 기능 커밋의 이동 전 경로를 구성했다. 다음 커밋은 같은 내용을 현재 경로로 옮긴다. 기존 사용자 변경을 삭제·덮어쓰거나 이력을 rebase하지 않는다.

[분리 선별·검증 정보](selection.json) · [이동 전후 목록](../../layout/20261101-role-folders-01/moves.json) · [현재 구조](../../../src/README.md#source-tree).

## 검증 근거

- 최종 제품 코드/프로젝트/리소스194개가 [폴더 재배치 때의 소스 해시](../../layout/20261101-role-folders-01/source-hashes.json)와 같다. 커밋을 위해 제품 코드를 수정하거나 시험을 새로 실행하지 않았다.
- [전체 솔루션 빌드·자동1261건·리소스/진입점](../../layout/20261101-role-folders-01/README.md)은 이전 동일 코드의 실제 실행이다.
- [실제 GUI/MQTT20항목](../../GUI/20261101-figma-live-02/README.md), [실제 Seq/MQTT14·기존 MQTT20·대화형 입력](../../LOG/20261101-trial-seq-01/README.md)을 연결한다. 이 수치를 한 번의 신규 시험으로 합산하지 않는다.
- 소스·문서·시험 도구의 diff 공백 검사는 통과했다. 원시 캡처 로그의 마지막 빈 줄은 실제 기록을 보존해 전체 raw-capture whitespace 검사에서 별도로 남긴다. 로그를 다듬어 새 시험 결과처럼 만들지 않는다.
- Git의 기존 텍스트 줄바꿈 필터를 적용해 객체를 기록한다. 디스크의 원시 파일 해시와 Git blob을 구분하며, 최종 작업 파일과 인덱스/커밋 트리를 대조한다.

## 보존·제외

`evidence/COM-01/20261031-task1-results-01/`의 기존 사용자 보기본35개는 내용 해시를 확인하고 게시에서 제외한다. 그대로 로컬에 남긴다. 빌드 생성물·개인 설정·`C:\process-plan` 문서 원본은 코드 커밋에 포함하지 않는다. 실패·중단 시험과 중요 재확인 근거는 기존 실행 ID로 보존한다.

## #46 완료 확인

- [PR #47](https://github.com/inhhlee/amr-control-system-demo/pull/47)은 develop에 `2339df1b1955b26c10f93295fcb3ee2622fc6131`로 병합됐다. 갱신한 origin/develop의 조상에 포함된다.
- 검토 head `4f25f3e`와 병합 트리는 `dc91b6c0c0e93f705cf9979dbe43575977a7beea`로 같다. 원래 완료조건7개·사용자 추가 범위·기존1141건/실제 연동 근거를 확인했다.
- 사용자 정리 요청에 따라 [Issue #46](https://github.com/inhhlee/amr-control-system-demo/issues/46)을 completed 종료했다. 새 Fleet 네이티브 F5/재개 클릭, A*·DB·운영/전체 과제는 기존 미검증/후속 범위로 유지한다.
- 작업/보존 브랜치·stash·복구 자료를 삭제하지 않는다. 제품 시험 재실행·새 기능 완료로 집계하지 않는다.

## 남은 단계

원격 게시 결과의 커밋·트리 일치와 브랜치 상태를 확인한 뒤 원본 일일업무에 기록한다. #58은 후속 PR·검토·develop 반영 전까지 열린 상태로 유지한다. 장애 알람 #50 개발은 현재 변경의 develop 반영 이후 작업으로 구분한다.
