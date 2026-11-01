# Seq Compose·환경변수·실행 안내 검증

## 1. 배경·목적

기존 Seq는 실행 중이지만 저장소에는 Mosquitto만 정의되어 있고, 기존 컨테이너 라벨의 Compose 원본 파일도 현재 경로에 없었다. 사용자 요청으로 새 PC에서 재현할 Seq 구성과 기존 서비스 재사용 안내를 추가했다. 관련 범위는 COM-03·사용자 추가 로그 요구와 D-10의 진단 로그다.

기준은 로컬 기능명세v0.13·공통데이터v0.12·결정사항v0.16 및 개발환경의 `trial-seq-20261101` 후속 기록이다. 통신 Payload·콘솔 환경변수 이름·로그 전송 정책은 변경하지 않았다. 새 설정은 로컬 개발·시험용이며 운영 인증·보존 정책 확정이 아니다.

## 2. 변경·검증 기준

| 대상 | 반영 내용 | 완료 기준 |
| --- | --- | --- |
| `docker/compose.yaml` | 고정 Seq2026.1, 선택 `logging` 프로필, loopback 포트, named volume | 새 Seq API와 실제 로그 검색 가능 |
| `.env.example` | Compose 포트·콘솔 URL·선택 API 키·실행 ID 예시 | 포트 변경 반영, 실제 개인 환경파일 제외 |
| `docker/README.md`·기존 실행 안내 | 새 설치/기존 재사용, PowerShell/Git Bash 환경변수, 종료·데이터 보존·실제 확인 | 링크·절차 연결, Compose와 호스트 앱의 환경변수 차이 명시 |

선택 이유는 기존 Mosquitto 사용자가 Seq를 필수로 실행하지 않도록 하고, 새 PC가 기존 PC의 절대 데이터 경로에 의존하지 않도록 하기 위함이다. 별도 Compose 파일도 가능하지만 두 서버를 같은 프로젝트에서 찾도록 선택 프로필을 사용했다. 기존 인스턴스를 직접 이관하면 같은 포트·데이터 접근을 정리해야 하므로 이번에는 이관하지 않았다. 성능·메모리 비교를 수행한 선택은 아니다.

## 3. 실행 환경과 절차

- 실행: 2026-11-02 00:48:47~00:49:33 +09:00. `develop` 기준 `5d0902febead29d6b49dcbb25b111752d4655d2a` + 이번 미커밋 구성·안내 변경.
- Windows, .NET SDK10.0.401, Docker Linux 엔진24.0.6, Seq API2026.1.17114. [실제 환경](environment.json)·[최종 소스/바이너리 해시](source-files.json).
- 입력: [포트 변경 예시](test.env.example), [실제 Compose 해석](resolved-compose.json), [이전 Mosquitto 정의](compose-before.yaml). 시험 프로젝트 `amr-seq-check-20261102-seq-compose-01`, Seq `127.0.0.1:59574`.
- 도구: [이번 실행 고정본](verify-setup.ps1). 시험 전 프로젝트 자원이 없는지 확인하고, 같은 Compose를 별도 프로젝트명·미사용 포트로 실행했다. 저장소의 실제 서비스·기존 PC Seq는 시작·종료·재생성하지 않았다.
- 빌드: `dotnet build AmrControlSystem.slnx --no-restore -m:1 -nr:false -p:UseSharedCompilation=false` 완료, 경고0·오류0. 전체 xUnit 자동시험은 이번 구성 변경에서 재실행하지 않았다.

## 4. 실제 결과

| 입력 → 예상 결과 | 실제 결과·판정 | 증거 |
| --- | --- | --- |
| 프로필 없이 해석 → Mosquitto만 기본 서비스 | 통과 | [구성 판정](verification.json) |
| 이전 Mosquitto와 비교 → 이미지·포트·마운트 동일 | 통과 | 이전/해석 Compose 및 구성 판정 |
| 예시 포트 변경 → loopback의 새 포트, `/data` named volume | 통과 | [해석 결과](resolved-compose.json) |
| 새 Seq 생성 → API 응답 | 통과 | [생성 로그](create.log)·환경 |
| 실제 trial/MQTT → 핵심 콘솔·Debug 수집·상태/명령 검색·Seq 불가 시 통신 지속 | **14/14 통과**, Debug Telemetry24건 포함 Seq 이벤트107건 조회 | [별도 실제 연동 판정](../20261102-seq-compose-01-trial/verification.json)·[실행 로그](trial.log) |
| 시험 컨테이너 중지·제거·재생성 → 기존 이벤트 유지 | 통과. 새 컨테이너 ID에서도 동일 이벤트 ID 조회 | [전후 ID](persistence.json)·[재생성 로그](recreate.log) |
| 시험 자원 정리 → 기존 컨테이너3개 보존 | 통과. ID·실행 상태·시작 시각 동일 | [이전](containers-before.txt)·[이후](containers-after.txt)·[정리](cleanup.log) |

구성 확인 **11항목** 안에 별도 실제 연동14항목의 통과 확인이 포함된다. 25개의 독립 제품시험으로 합산하지 않는다. `.env`·`.env.local` 제외와 `.env.example` 추적 허용, 문서 링크와 `git diff --check`도 확인했다. [판정 원본](verification.json)은 실패·정리 오류 없이 passed다.

## 5. 결과·한계

요청한 저장소 구성·환경변수 예시·실행 안내와 해당 실제 검증을 완료했다. C# 제품 코드는 변경하지 않았다. 기존 사용자 Seq와 데이터는 이관·초기화하지 않았고 시험용 Seq·볼륨만 정리했다.

브라우저 화면 수동 조작·새 화면 캡처, 인증 서버의 API 키 수집·조회, 외부 PC의 최초 이미지 다운로드, 운영 배포·백업 복구는 이번 확인 범위 밖이다. API 조회로 실제 수집을 확인했으며 문서가 존재한다는 이유만으로 확인한 결과가 아니다. 커밋·푸시·Issue·PR은 이번 요청에서 수행하지 않았다.

검토 시에는 기본 서비스에 Seq가 강제 포함되지 않는지, `.env`를 호스트 .NET 앱이 자동으로 읽는다고 안내하지 않는지, 기존 Seq와 새 볼륨을 구분하는지 확인한다. 기존 서비스 이관이나 여러 PC 공유가 필요하면 인증·데이터 이전·접근 정책을 별도 검토한다.

재현은 [현재 실행 안내](../../../docker/README.md)를 따른다. 이 폴더는 고정 실행 근거이며 덮어쓰지 않는다. 시험 도구 재실행 시 새 실행 폴더·RunId를 사용한다.
