# #34 PR 분리 확인

- 범위: #33 계산 위에 실제 시간 실행 루프·순차 위치 갱신·도착·취소·로그를 연결한다. MQTT·지도·Communication 프로젝트 참조는 포함하지 않는다.
- 원본 #33: 71160f412624f829f8b5673c409da5a4accf202c. PR #36의 정리 커밋 d4bdfabf9945695a01ac86a80b61a0befafbd72a는 이 커밋과 전체 tree가 같다.
- 원래 전체 구현 a8934f0bfed803cee47d7c2e13775ba648e0862a는 codex/preserve-simulator-mqtt-map-20261021에 보존한다. 원래 작업 폴더·인덱스·로컬 브랜치는 변경하지 않았다.
- #34 Application의 설정 검사·루프 호출·예외 처리 흐름을 복원했고 Runner의 후속 MQTT snapshot callback만 제외했다. 설정·이동 모델·기존 시험·Program·프로젝트 파일은 #33 원본과 같은 blob이다.
- 코드 분리 후 결과: **표준 자동시험 135/135 통과**, 실패·skip 0. 이동41 + 기존 설정/Application88 + Runner6이다. 이전129/135/151과 합산하지 않는다.
- 실제 별도 콘솔 **6/6 통과**: 기본 도착, 설정 변경 도착, 오류 거부, 이동 없는 경로, 이동 중 CTRL_C_EVENT, Int64 최대 갱신 간격 중 CTRL_C_EVENT. 신호 시험이며 사용자의 직접 키보드 조작과 구분한다.
- 빌드는 성공했다. 첫 복원의 NuGet 취약성 조회는 네트워크 제한으로 NU1900 경고가 있었다. 컴파일 오류는 없었다. 제한 환경의 VSTest는 90초 testhost 연결 실패로 중단돼 [원본 실패 TRX](split-runner.trx)를 보존했다. 같은 DLL을 권한 확장 환경에서 --no-build --no-restore로 실행해 [135건 TRX](split-runner-elevated.trx)를 확보했다.
- 실행별 입력·예상/실제·종료 코드·원본 로그는 [실제 6건](../20261021-pr34-split-console-01/verification.json), 소스·DLL·증거 해시는 [식별 manifest](source-and-evidence.json)에 연결했다.
- 검증은 임시 사본에서 수행했다. 콘솔 원본 verification.json의 Branch=main·Head=7426d64는 상위 문서 저장소를 읽은 값이므로 제품 버전으로 사용하지 않는다. 제품 식별은 위 source manifest의 Git blob과 원본 SHA를 따른다. 이 두 raw 필드는 수정하지 않았다.
- 기존 [#34 실행 결과](../20261021-runner-01/README.md)·초기 실패·콘솔01/02/03·설정 회귀 증거는 원본 blob 그대로 재사용한다. 기존 기록은 당시 버전·시각의 결과이고 이번 분리 후 결과는 이 문서의 새135/6건이다.
- #28 원래 완료조건과 F01~05: 설정·초기 생성·오류 검사·설정 변경은 기존88건과 실제 normal/changed/invalid, 이동·도착 후 유지는 이동41건, 실행·시계·취소는 Runner6건과 실제6건으로 연결한다.
- 미실행: MQTT·GUI·DB·실장비·통신 전체 회귀·운영 부하. 별도 PR의 리뷰·develop 반영·Issue 종료도 아직 수행하지 않는다.
