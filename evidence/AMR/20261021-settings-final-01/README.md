# AMR-01 / Issue #32 설정 로드·초기 생성 검증

## 1. 목표와 적용 기준

- 실행자: Codex AI 보조. 별도 사람의 승인 리뷰 아님.
- 범위: JSON 로드·검사 → AMR 1대 초기 생성 → 값·경로·IDLE·현재 속도 0 출력 → 종료.
- 기준: 과제2 과제계획 v0.10 초안, 기능명세 v0.9 초안 AMR-01, 공통데이터명세 v0.6 초안 4절, 결정사항 v0.9 초안 D-12와 2026-10-21 ID 검사 보완.
- 문서 원본: C:\process-plan. 공유 URL 없음. 원문 요구와 사용자 승인 개발 기준을 구분하며, 실제 JSON 필드명·정수 자료형·추가/중복 필드 거부는 이번 구현 선택이다.
- #28을 총괄로 두고 #32 → #33 → #34로 분할한 현재 사용자 요청을 적용한다. 문서 원본의 단일 Issue 표현은 분할 전 기록이다.
- 제외: 이동 계산·도착(#33), 실제 실행 루프·이동 중 Ctrl+C(#34), MQTT·GUI·Core·DB·다중 AMR·장애·명령/ACK.
- 별도 spec_revision/verification_index 추적기는 이 Simulator에 아직 없으므로 Issue·문서 버전과 소스 해시로 연결한다.

## 2. 환경과 코드 버전

| 항목 | 실제 값·근거 |
| --- | --- |
| 실행일 | 2026-10-21. 프로세스 실행 시작/종료의 offset 포함 원본은 console verification.json |
| OS / SDK | Windows 10.0.26200 / SDK 10.0.401, net10.0. 상세 runtime: environment.json |
| 기준 커밋 | d5183566cfba8ced1cdd24218efc48f09472397d + 이번 미커밋 변경 |
| 실제 작업 브랜치 | feature/28-simulator-first-run |
| 필요한 구현 브랜치 | feature/32-simulator-settings. git flow feature start가 .git/FETCH_HEAD Permission denied로 실패 |
| 브랜치 제한 대응 | Git 메타데이터를 변경하지 않고 허용된 작업 트리에서 #32 소스·시험·근거를 준비. 커밋·게시 전 #32 브랜치 전환과 최신 develop 대조 필요 |
| 파일 버전 | source-sha256.json에 소스·프로젝트·설정·시험·실행 도구 SHA256 |
| 실행 DLL | environment.json의 ProductDllSHA256·TestsDllSHA256, 프로세스 검증 JSON의 DllSHA256 |
| 패키지 | 새 제품 패키지 없음. 전용 시험은 기존과 같은 Test.Sdk 17.14.1 / xUnit 2.9.3 / adapter 3.1.4 |
| Broker / Topic / QoS | 해당 없음. Broker·GUI·Core 없이 독립 초기화만 실행 |
| 테스트 대역 | 제품 MQTT 대역 없음. xUnit 시험에서 임시 설정 파일·TextWriter 사용, 별도 Console 프로세스 검증은 실제 DLL 실행 |

## 3. 실행과 입력

저장소 루트에서 실행했다. 동일한 최종 C# 소스를 빌드하고 다음 두 검증을 수행했다.

~~~powershell
dotnet build AmrControlSystem.slnx --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet build tools/RunSimulatorTests/RunSimulatorTests.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet tools/RunSimulatorTests/bin/Debug/net10.0/RunSimulatorTests.dll evidence/AMR/20261021-settings-final-01/tests.json
pwsh -NoProfile -File tests/manual/AMR01/Verify-Settings.ps1 -RunId 20261021-settings-console-02
~~~

- 단일 MSBuild 노드와 직접 컴파일은 현재 환경의 서버 IPC 제한을 피하는 실행 옵션이다. 제품 구성이나 시험 기대값을 완화하지 않았다.
- 일반 VSTest는 아래 실패 기록과 같이 로컬 TCP 연결이 차단됐다. 별도 실행 도구는 기존 xUnitTestFramework 엔진으로 같은 시험 어셈블리의 Fact·Theory를 실행하며 결과 메시지·실패·skip을 수집한다. 0건·skip·엔진 오류·결과 개수 불일치는 성공으로 처리하지 않는다.
- RunSimulatorTests는 솔루션 외부의 보조 도구다. VSTest 어댑터/testhost 성공으로 대체 집계하지 않는다.
- 정상 설정 스냅샷은 ../20261021-settings-console-02/normal.json. 원본 config/simulator.single.json과 해시를 연결한다.
- 변경 설정 스냅샷은 changed.json. 같은 DLL에서 초기 좌표·속도·경로만 변경한다.
- 오류 입력 스냅샷·stdout/stderr·기대/실제 종료 코드는 같은 디렉터리 verification.json의 Cases와 파일로 연결된다. missing-file.json은 파일 없음 시험이므로 의도적으로 생성하지 않는다.

## 4. 실제 결과와 판정

| 확인 | 입력·예상 | 실제 결과 | 판정·근거 |
| --- | --- | --- | --- |
| 솔루션 빌드 | 기존 통신 프로젝트와 새 Simulator 시험 프로젝트 포함, 오류 없음 | 경고 0·오류 0 | 통과 / solution-build.log |
| 보조 도구 빌드 | 같은 xUnit 엔진 실행 도구 빌드 | 오류 0, NU1900 경고 1 | 빌드 통과 / runner-build.log. NuGet 취약성 서버 확인은 미검증 |
| xUnit 프로세스 내 실행 | 정상·변경·누락·자료형·ID·비유한 수·경로·파일/인코딩·출력·취소 등 | 83건, 실패 0, skip 0, 엔진 오류 0 | 통과 / tests.json |
| AMR2-F01 초기화 | AMR_001, 초기(-1,0), 설정 속도1, 경유점2, 100ms → IDLE·현재 속도0 | 모든 설정과 경로 순서 출력 일치, 종료0 | 통과 / console-02 normal.stdout.log·verification.json |
| AMR2-F02 초기화 부분 | 초기(12,-7), 설정 속도2.5, 경유점(-8,9) → 같은 DLL에서 변경 반영 | 설정·초기값·경로 출력 변경, IDLE·현재 속도0, 종료0 | 통과 / console-02 changed.stdout.log |
| AMR2-F05 실제 거부 | 누락·ID오류·속도0/음수/overflow/문자열·소수 주기·좌표null·문법·없는 파일 → 생성 전 거부 | 오류 10건 모두 stdout 비어 있음, 항목·이유 stderr 출력, 종료1 | 통과 / console-02 각 stderr.log·verification.json |
| Console 전체 | 정상2·오류10 → 총12건 예상 결과 | 12/12 Passed=true | 통과 / ../20261021-settings-console-02/verification.json |
| 초기화 취소 | 사전 취소 토큰 → 생성 성공과 구분 | InitializationCanceled, 종료0, 오류 출력 없음 | 자동시험 통과 / tests.json. 실제 키보드 Ctrl+C는 미실행 |
| 일반 VSTest | testhost 연결 후 시험 실행 | testhost/collector 로컬 TCP 연결 권한 오류10013, timeout·중단 | 환경 제한 / unit-01 TRX·unit-02 failure-logs.zip. 통과로 집계하지 않음 |

정상 프로세스 실제 출력:

~~~text
ConfigurationLoaded AmrId=AMR_001 InitialX=-1 InitialY=0 SpeedMps=1 UpdateIntervalMs=100 WaypointCount=2
Waypoint Index=0 X=1 Y=0
Waypoint Index=1 X=1 Y=2
AmrInitialized AmrId=AMR_001 X=-1 Y=0 State=IDLE CurrentSpeedMps=0
InitializationCompleted
~~~

위 출력은 이동·도착·MQTT 송신을 뜻하지 않는다. AMR2-F02의 실제 이동 차이와 F03·F04는 #33·#34에서 확인한다.

## 5. 실패 이력·검증 한계

- ../20261021-settings-unit-01/simulator-initial.trx: 최초 일반 dotnet test는 testhost에 연결하지 못해 중단. 시험 통과 0건으로 보존한다.
- ../20261021-settings-unit-02/failure-logs.zip: 진단 실행은 collector 로컬 소켓 연결에서 SocketException 10013으로 반복 실패했고 종료했다. 원본 로그를 압축 보존했으며 압축 안의 길이·SHA256이 원본과 일치함을 확인했다. failure-log-manifest.json에 원본 길이·해시가 있다. 반복 오류로 커진 원문을 잘라내거나 폐기하지 않았다.
- ../20261021-settings-inprocess-01/: 보조 도구 초기 작성 중 xUnit3000 상속 규칙과 해당 타입의 namespace 오류를 수정했다. 초기 build.log·build-direct.log·build-fixed.log를 보존한다. product 코드 결함과 구분한다.
- 같은 디렉터리 results.json의 82건 통과는 Unicode 필드명 보완 전 코드의 기록이다. 최종 판정에는 현재 tests.json의 83건만 사용한다.
- ../20261021-settings-console-01/은 앞선 코드의 실제 12건 기록이다. 최종 판정에는 console-02를 사용한다.
- 빌드 도구 복원 시 api.nuget.org 취약성 정보 접근 불가 NU1900 경고가 있었다. 확인을 끄거나 성공으로 표시하지 않았다. 새 제품 패키지를 추가하지 않았다.
- 기존 통신 제품 소스를 변경하지 않았고 솔루션 빌드에 포함했다. 기존 통신 자동시험 768건은 이번 실행 결과가 아니며 재실행하지 않았다.
- 현재 표준 VSTest·실제 키보드 Ctrl+C·외부 제품 연동은 미검증이다. 소켓 통신이 허용된 일반 환경에서 dotnet test를 재실행할 수 있다.
- C:\process-plan의 계획·일일업무 원본은 읽기만 가능해 이번 실제 결과를 아직 반영하지 못했다. 날짜별 새 중복 업무 파일을 만들지 않았다.
- 커밋·푸시·PR·병합·Issue 종료는 수행하지 않았다. 현재 로컬 구현·시험 준비 결과와 원격 게시·반영 완료를 구분한다.
- 검증 후 GitHub #32 본문에 로컬 결과를 반영하려던 호출은 오류를 반환했다. 재조회에서 본문이 바뀌지 않았고 open 상태임을 확인했다. 따라서 원격 완료 체크·결과 기록은 반영되지 않았으며 이 로컬 근거를 사용해 후속 게시 단계에서 갱신해야 한다.
- 이후 2026-10-21 사용자가 시퀀스 다이어그램을 Issue 해결 내용으로 게시하도록 명시 요청해 [Issue #32](https://github.com/inhhlee/amr-control-system-demo/issues/32) 본문에 해결 방식·아키텍처·시퀀스·실제 결과·미검증·남은 절차를 게시했다. 재조회에서 준비한 본문과 일치하고 Mermaid 블록 2개·기존 완료조건 6개 미체크·open 상태임을 확인했다. 이는 Issue 설명 게시 결과이며 소스·상세 로그의 커밋/푸시나 PR 반영 결과가 아니다. 앞선 게시 실패 기록을 보존하고 이번 게시 단계에서는 제품 시험을 재실행하지 않았다.

## 6. 설계 판단·리뷰 근거

- JsonDocument로 필수값·자료형·범위를 명시적으로 읽어 오류의 JSON 경로를 제공한다. 누락 숫자를 기본 0으로 받거나 ID를 trim해 보정하지 않는다. 필드명 대소문자와 추가·중복 필드도 검사한다.
- 설정 객체는 검사 후 생성하고 속성을 읽기 전용으로 둔다. 경유점 배열은 방어 복사 후 읽기 전용 목록으로 노출한다. JsonDocument는 Parse 종료 시 Dispose하며 값 객체·문자열만 유지한다.
- 대안은 required 속성과 JsonSerializer DTO·후속 validator다. 필드 정의 코드가 줄 수 있지만 누락/범위/경로별 오류와 중복 처리 규칙은 여전히 정해야 한다. 현재 작은 스키마에서 정밀한 오류 경로를 우선했다. 전체 문서·경유점 배열과 방어 복사를 메모리에 올리는 비용이 있고, 성능·메모리 벤치마크는 수행하지 않았다. 대규모 경로·스트리밍 요구가 생기면 재검토한다.
- 이번 초기 모델은 불변이고 백그라운드 작업·소켓·구독이 없다. Program이 CTS·Ctrl+C 이벤트의 수명을 소유하고 RunAsync를 기다린 후 이벤트를 제거한다. #33·#34가 상태 변경과 루프의 단일 소유·취소 후 실제 종료 책임을 추가해야 한다.

예상 PR 질문:

1. 왜 현재 속도를 설정 속도와 따로 두나? 설정 속도는 이동 시 사용할 값이고 초기 현재 속도는 계약상 0이다. 정상·변경 설정과 자동시험에서 두 값을 각각 확인했다.
2. 왜 이동이나 MQTT를 붙이지 않았나? #32의 완료조건은 설정·초기 생성이다. 이동은 #33, 루프는 #34이고 MQTT는 세 Issue 밖의 후속이다.
3. xUnit 통과를 일반 dotnet test 성공으로 봐도 되나? 아니다. 같은 xUnit 엔진의 83개 시험 실행은 확인했지만 VSTest 어댑터/testhost 전송 경로는 환경 제한으로 미검증이다. 일반 환경에서 표준 명령의 재실행이 필요하다.

## 7. #32 완료조건 연결·남은 작업

| #32 조건 | 근거·상태 |
| --- | --- |
| 정상 필수 설정 로드·AMR 1대 생성 | xUnit·normal 프로세스 통과 |
| 초기 IDLE·현재 속도0·설정 출력 | normal·changed 출력 통과 |
| 초기 좌표·속도·경로 변경 반영 | changed 프로세스 통과; 이동은 후속 |
| 잘못된 값·파일/JSON 거부·오류 종료 | xUnit 경계 시험·오류 프로세스10건 통과 |
| 음수 좌표·설정/현재 속도 구분 | 정상·변경·자동시험 통과 |
| 필요한 시험·실제 초기화·버전·미검증 연결 | 이 문서·TRX/JSON/log·source-sha256.json·environment.json |

로컬 기능·위 검증 범위는 충족했다. 표준 VSTest 재실행과 #32 작업 브랜치 전환·최신 develop 대조, 원본 업무 기록 반영, 사용자 요청에 따른 게시·리뷰·반영 단계는 남아 있다. 선행 #32 PR의 develop 반영과 근거 확인 후 #33을 시작한다.
