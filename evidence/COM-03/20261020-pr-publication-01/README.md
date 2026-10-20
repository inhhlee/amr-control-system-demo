# COM03-C #6 커밋·푸시·PR 게시 확인

2026-10-20 사용자 요청으로 같은 `feature/6-mqtt-keepalive`의 구현·시험·근거를 게시했다. **[PR #31](https://github.com/inhhlee/amr-control-system-demo/pull/31)은 develop 대상·open·미병합이고, [#6 Issue](https://github.com/inhhlee/amr-control-system-demo/issues/6)는 원래 완료조건 8개와 최종 증거/PR 연결을 갱신한 뒤 open으로 유지했다.**

- 코드·시험·증거 커밋: [bccb461554f14dd5b7456c07f4dec4d9ea9a3a99](https://github.com/inhhlee/amr-control-system-demo/commit/bccb461554f14dd5b7456c07f4dec4d9ea9a3a99), 메시지 `feat: MQTT 연결 유지와 끊김 감지 검증 (#6)`. 302개 파일은 #6 범위이며 원본 실패/중간 실행도 포함한다.
- Git Flow: `git fetch origin`으로 develop d777ab7과 기존 브랜치를 확인, 명시적 파일 스테이징·검토 후 커밋, `git flow feature publish 6-mqtt-keepalive`로 최초 푸시·추적 설정. 강제 푸시·rebase·feature finish·병합은 수행하지 않았다.
- [최종 시험 소스와 커밋 대조](committed-source-audit.json): 72개 SHA-256이 C4 시험 당시 바이트와 동일하고 Git clean 필터를 적용한 blob도 게시 커밋과 동일하다. CRLF/LF 변환과 내용 일치를 구분했다. PR 게시 이후의 추가 커밋은 이 안내·README·근거/본문의 게시 상태만 갱신한다.
- 최종 제품 검증은 [C4 768건/경고·오류0](../20261020-c4-unit-02/README.md) 및 [A~C 실제 MQTT 근거 취합](../20261020-ac-summary-01/README.md)이다. 제품·시험 소스가 같아 게시 단계에서 제품 시험을 재실행하지 않았다.
- [게시 상태 조회](publication.json): PR base/head·open/merged=false, Issue open·8/8 체크·PR 링크를 실제 조회했다. 문서 추가 커밋 전 코드 head와 후속 문서 변경을 구분한다.
- 스테이징 검사에서 기존 검증 도구 `Verify-KeepAlive.ps1`의 끝 빈 줄을 확인했다. 시험본을 유지하기 위해 그대로 보존했고 소스 검사는 그 항목만 제외한 Git 공백 규칙으로 통과했다. 원본 실행 로그의 공백도 변경하지 않았다.
- GitHub 설명은 PR 양식에 따라 관련 Issue·변경·실제 시험·8조건·한계를 기록했다. 자동/대역 시험과 실제 Broker/프록시를 구분하며 인증 Broker 거부·Seq 새 진단 필드·모든 경합/부하·시각 변경·운영 SLA·자동 복구 등을 통과로 확대하지 않는다.

남은 단계는 리뷰와 요청된 develop 병합, 반영 후 Issue 종료다. 코드 저장소의 게시와 C:\process-plan 원본 업무 기록 갱신을 구분하며 문서 저장소 커밋은 수행하지 않았다.
