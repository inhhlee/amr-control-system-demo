# Codex 없는 실행 환경의 경로 독립성 검증 — #42

## 코드와 환경

기준 develop은 `4bd0be313c866c7ed2ab629f9ec61ab4891267ef`다. 이 기준의 깨끗한 로컬 복사본에 이번 수동 시험 도구·안내 변경을 적용해 2026-10-22 KST에 시험했다. 제품 C# 소스·자동시험·패키지·설정은 바꾸지 않았다. 시험 후 게시 단계에서 같은 결과를 재사용하며 제품 시험을 새로 실행한 것으로 집계하지 않는다.

Windows의 일반 설치 .NET SDK 10.0.401, PowerShell 7.6.7, Git, Docker CLI·Linux 컨테이너 엔진을 사용했다. 과거 자료 검사는 일반 설치 Python 3.9로 별도 실행했다. PATH에서 Codex 도구를 제외하고 CODEX 계열 환경변수·PYTHONPATH·NODE_PATH·Seq 설정을 제거했다. `rg`가 없는 것도 확인했다. SDK 사용자 폴더와 NuGet 패키지 폴더는 새 임시 위치를 사용했고 기존 bin/obj를 복사하지 않았다.

시험 위치는 임시 폴더 아래 `새 PC test/amr demo`다. 실행 도구 경로·복원·빌드·자동시험은 ZIP의 `portable.log`에 있다. 이는 **동일 PC의 경로·도구 분리 시험**이며 실제 두 번째 물리 PC에서 실행한 결과가 아니다. 일반 필수 도구 설치 후 다른 PC에서 따라 할 절차는 [BUILDING.md](../../../BUILDING.md)에 있다.

`tested-files.json`은 게시할 src/tests/tools/config/docker/global.json/솔루션과 시험 복사본을 대조한 목록이다. RawBytesEqual과 CRLF→LF만 다른 경우를 구분하며 각 원시 SHA256을 보존한다. Git의 줄바꿈 변환을 소스 로직 변경으로 집계하지 않는다. raw 환경 기록의 Head/Branch는 기준 커밋 및 당시 임시 복사본 값이고, 게시 소스와의 연결은 이 목록으로 확인한다. 보조 시험 도구의 기존 브랜치 검사를 충족하려고 임시 복사본에서만 `feature/6-mqtt-keepalive` 브랜치를 사용했다.

## 입력·예상·실제 결과

| 구분 | 입력·예상 | 실제·판정 | ZIP 내 근거 |
| --- | --- | --- | --- |
| 복원·빌드 | 새 패키지 캐시, 전체 솔루션, 정상 복원·빌드 | 성공, 경고0·오류0 | portable.log |
| 자동시험 | 전체 솔루션, 실패·skip0 | Simulator154 + 통신768 = 922건 통과 | unit-results/*.trx |
| 실제 이동 | 기본/변경 경로·오류 설정·이동 없는 경로·이동 중 및 긴 간격 취소 | 6개 통과, 도착/취소0·오류 거부1 | AMR/portable-run/verification.json 및 입력·로그 |
| 실제 MQTT | 기본/변경/처음부터 도착·Retain 없음·독립 송신과 취소·단절·최초 접속 실패 | 7개 통과, 실제 관제 모델과 좌표·상태·속도 대조 | AMR/portable-mqtt/verification.json 및 입력·송수신 로그 |
| 도구 경로·소스 목록 | 공백·한글 경로의 숨김 자식 프로세스와 프록시, rg 없이 목록 수집 | 모든 수동 스크립트 구문·5개 수집식 확인, bin/obj/TestResults 제외 | tools-02.log |
| 보조 실제 통신 | KeepAlive12초·응답 차단 C3a·Broker 종료와 새 세션 복구 | 3개 도구 통과, 기존 컨테이너 상태 보존·소유 자원 정리 | COM-03/portable-keepalive-02, portable-proxy, portable-shutdown |
| 과거 자료 검사 | 변경 없는 4bd0be3 복사본, 일반 Python | 보존70·manifest69+예상 제외1·과거 소스54 일치 | tools-02.log 마지막 JSON |

처음 보조 KeepAlive 시험에서 관찰 시간을 기본12초보다 짧은10초로 지정해 두 번째 Ping 쌍 확인이 실패했다. 기대 결과나 제품을 바꾸지 않고 기본12초와 새 RunId로 재실행해 통과했다. 최초 `tools.log`와 `COM-03/portable-keepalive/` 실패를 ZIP에 함께 보존했다. 실패 시도는 통과 건수에 포함하지 않는다.

## 원본과 무결성

[raw-results.zip](raw-results.zip)에 이번 실행 원본을 모았다. [verification.json](verification.json)은 집계와 ZIP SHA256, [tested-files.json](tested-files.json)은 시험 파일 대조 결과다. 과거 기존 evidence 파일은 수정하지 않았다. 저장소 루트의 PowerShell에서 압축파일을 확인할 수 있다.

```powershell
$e = 'evidence/PORTABILITY/20261022-01'
$v = Get-Content "$e/verification.json" -Raw | ConvertFrom-Json
if ((Get-FileHash "$e/raw-results.zip").Hash -ne $v.RawZipSHA256) { throw 'ZIP hash mismatch' }
```

## 확인하지 않은 범위

실제 두 번째 물리 PC, Linux/macOS, 최소 PowerShell 버전의 별도 실행, Seq 실서비스, GUI는 이번 시험이 아니다. COM01/COM05의 전체 장애 시험을 새로 수행한 것은 아니며 변경한 소스 수집식과 경로 처리 범위를 확인했다. 과거 Python 검사는 제품 실행 시험이 아니다. GitHub CI를 실행하거나 통과로 집계하지 않았다. 업무 문서·일일·주간 기록은 작성하지 않는다.
