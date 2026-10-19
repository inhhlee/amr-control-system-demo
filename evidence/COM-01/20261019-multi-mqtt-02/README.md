# COM01-B 실제 MQTT 실행 02 — 검증 도구 실패

첫 세션 수신·CTRL_C_EVENT 종료 뒤 두 번째 세션 발행에서 PowerShell native command의 콘솔 핸들 오류로 중단됐다. 전체 통과 근거로 사용하지 않는다. [실패 로그](failure.log), [컨트롤러 결과](controller-result.json)와 당시 입력·콘솔·소스 해시를 보존한다.

검증 도구의 Docker 실행을 명시적 표준 입출력의 ProcessStartInfo로 보완했다. 제품 소스 변경 없이 [새 실행 03](../20261019-multi-mqtt-03/README.md)에서 전체 시나리오를 통과했다.
