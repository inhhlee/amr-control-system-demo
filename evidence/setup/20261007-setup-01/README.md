# 개발 기반 설정 검증결과 — 20261007-setup-01

## 1. 목표와 적용 기준

- 대상: [Issue #2 — 개발환경 재사용과 최소 프로젝트 구성](https://github.com/inhhlee/amr-control-system-demo/issues/2).
- 적용 범위: 사용자의 2026-10-07 지시에 따라 현재 코드 프로젝트에 독립적인 SDK·솔루션·라이브러리·콘솔·시험 프로젝트를 구성한다.
- 제품 기능 ID: 해당 없음. 후속 COM-03의 연결 기능과 실제 Broker 통신은 이번 검증에 포함하지 않는다.
- 완료 기대: 복원·빌드 성공, 콘솔 준비 안내와 정상 종료, 시험 탐색 구성 확인, 소스·증거 추적과 생성물 제외 확인.
- 이전 미커밋 변경: AGENTS.md 보완, PR 양식, .gitignore를 보존하고 같은 작업 범위에서 정리했다. 사용자 요청에 따라 설정 안내의 외부 코드 저장소 참조를 제거했다.

문서 원본은 현재 접근 가능한 `C:\plan`에서 읽었다. 아래 버전 차이를 확인했으며, 읽지 못한 최신 문서를 확인한 것으로 처리하지 않았다. 이번 최소 구성은 Issue 본문의 구체적인 대상·버전·시험 조건과 사용자의 현재 프로젝트 설정 지시를 기준으로 삼았다.

| 문서 원본 | 확인한 버전 | Issue가 참조한 버전 |
| --- | --- | --- |
| `C:\plan\관제시스템 과제 진행\과제1\과제계획.md` | v0.9 초안 | v0.10 초안 |
| `C:\plan\관제시스템 과제 진행\개발환경.md` | v0.5 | v0.6 |
| `C:\plan\기준\개발 진행 절차.md` | v0.0 | v0.4 |
| `C:\plan\관제시스템 과제 진행\명세\기능명세.md` | v0.7 초안 | v0.7 |
| DEMO-SETUP | 현재 문서 폴더에서 찾지 못함 | v0.1 |

## 2. 실행 환경과 코드

| 항목 | 실제 값 |
| --- | --- |
| 실행 ID | 20261007-setup-01 |
| 시작 시각 | 2026-10-07 13:55:33 +09:00 (Asia/Seoul) |
| OS / 도구 | Windows 10.0.26200 x64 / PowerShell 7.6.5 / Git 2.55.0.windows.5 |
| SDK / 런타임 | .NET SDK 10.0.401 / .NET 10.0.12 |
| 작업 위치 | `C:\amr-control-system-demo` |
| 브랜치 | `codex/2-dev-foundation` |
| 기준 커밋 | `7f89908c4f1b4a0d73c8a9475ad84aa5825cbf84` + 이번 미커밋 변경 |
| 파일 식별 | [source-files.sha256](source-files.sha256)에 작성 파일의 SHA-256 기록 |
| 패키지 | MQTTnet 5.2.0.1603 / Microsoft.NET.Test.Sdk 17.14.1 / xunit 2.9.3 / xunit.runner.visualstudio 3.1.4 |
| NuGet | nuget.org와 Visual Studio 오프라인 소스 등록 확인. 설정 조회·복원은 권한 승인 후 실행 |
| Docker / Broker | CLI와 기본 설치 경로에서 Docker를 찾지 못함. Broker 상태·관리 위치·주소 미확인 |
| Topic·QoS·Retain·대역 | 해당 없음. MQTT 기능을 실행하지 않음 |

상세 환경은 [environment.log](environment.log)와 [dotnet-info.log](dotnet-info.log)에 기록했다.

## 3. 입력과 절차

모든 명령의 작업 위치는 `C:\amr-control-system-demo`다. 소스 파일과 프로젝트 정의가 입력이며, 빌드는 기본 Debug 구성이다.

| 시험 ID | 명령·절차 | 예상 결과 |
| --- | --- | --- |
| SETUP-01 | `dotnet restore AmrControlSystem.slnx --verbosity minimal` | 세 프로젝트 의존성 복원, 종료 코드 0 |
| SETUP-02 | `dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal` | 세 프로젝트 빌드, 종료 코드 0 |
| SETUP-03 | `dotnet run --project src/Amr.Communication.Console --no-build` | `AMR 관제시스템 개발 준비가 완료되었습니다.` 한 줄, 종료 코드 0 |
| SETUP-04 | `dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --list-tests` | 시험 어댑터 로드·탐색 완료. 아직 시험 0건 |
| SETUP-04 진단 | 같은 탐색 명령에 `--diag evidence/setup/20261007-setup-01/test-discovery-diagnostic.log` 추가 | 어댑터 누락과 실제 시험 0건을 구분 |
| SETUP-05 | 실제 bin·obj 파일 및 소스·증거에 `git check-ignore` 적용, 작성 파일 공백·참조 확인 | 생성물 제외, 소스·공유 예시·증거 추적 가능 |

## 4. 실제 결과와 판정

| 시험 ID | 실제 결과 | 판정 | 증거 |
| --- | --- | --- | --- |
| SETUP-01 | 세 프로젝트 복원, 종료 코드 0 | 통과 | [restore.log](restore.log) |
| SETUP-02 | 세 프로젝트 빌드, 경고 0개·오류 0개, 종료 코드 0 | 통과 | [build.log](build.log) |
| SETUP-03 | 콘솔 DLL 로딩 중 `0x800711C7`, 종료 코드 -532462766. 권한 승인 후 일반 사용자 환경 재시도도 동일 | 실패 — 실행 환경 정책 | [첫 실행](console.log), [재시도](console-retry.log), [Code Integrity 이벤트](code-integrity.log) |
| SETUP-04 | xUnit VSTest Adapter 3.1.4가 시험 어셈블리를 탐색. TotalTests=0, IsAborted=false, testhost 종료 코드 0 | 탐색 구성 통과; 제품 시험 미실행 | [탐색](test-discovery.log), [진단 재시도](test-discovery-retry.log), [진단 상세](test-discovery-diagnostic.log) |
| SETUP-05 | 실제 bin·obj 파일 162개 제외, 소스·증거·공유 예시 27개 추적 가능, 개인 설정·임시 결과 패턴 6개 제외, 로컬 링크 43개와 작성 파일 공백 확인 | 통과 | [repository-checks.log](repository-checks.log) |

시험 탐색 진단에는 `Microsoft.Diagnostics.NETCore.Client` 타입 로딩 경고가 있다. 해당 실행에서 xUnit 어댑터 등록·전체 탐색·정상 종료를 확인했다. 기능 시험이 추가되면 실제 실행도 별도로 확인해야 한다.

## 5. 문제와 후속 조치

- 콘솔 실행: Windows Code Integrity 이벤트 3033·3077에서 생성된 콘솔 DLL이 서명 수준 또는 정책 요구를 충족하지 못한 것을 확인했다. 실행 파일의 Zone.Identifier 스트림은 없었다. 코드 컴파일 실패와 구분한다.
- OS 정책을 변경하지 않았다. 이 PC에서 개발 빌드를 실행하도록 허용하는 정책을 확인한 뒤 SETUP-03을 다시 수행해야 한다. 출력·정상 종료는 아직 검증되지 않았다.
- Docker는 PATH와 기본 설치 위치에서만 확인했다. 미발견을 PC 전체 미설치로 단정하지 않는다. Broker는 현재 설정·상태·관리 위치를 확인할 수 없으며, 후속 연결 작업 전에 확인해야 한다.
- 최신 원본 문서 위치·버전은 후속 작업의 기준을 확인할 때 정리한다. 이번 설정에서 계획·명세 원본을 변경하지 않았다.
- 커밋·푸시·PR 생성·리뷰·병합은 사용자가 보류한 단계다.

## 6. 종합 결과

현재 프로젝트의 SDK·패키지 참조·솔루션·세 프로젝트 구성과 복원·빌드·xUnit 탐색을 확인했다. 콘솔 실행 성공 조건이 충족되지 않아 준비 Issue 전체 완료로 표시하지 않는다. 기능 시험, 실제 MQTT 통신, COM-03 완료는 판정하지 않았다.

관련 실행 안내: [저장소 README](../../../README.md). 실행 종료 시각과 최종 변경 목록은 [repository-checks.log](repository-checks.log)에 기록한다.
