# 팀 협업 규칙

- 작성일: 2026-10-08
- 버전: v0.3 초안 — feature/ 이름 규칙과 Git Flow 브랜치 역할 반영
- 대상: [inhhlee/amr-control-system-demo](https://github.com/inhhlee/amr-control-system-demo)의 Issue·코드·시험·PR 작업

팀원이 작업을 나누고 결과를 인계할 때 사용하는 안내다. 기존 [AGENTS.md](AGENTS.md), [PR 양식](.github/pull_request_template.md), [.gitignore](.gitignore)를 바탕으로 작성했다. 전용 협업 규칙 빈 양식은 확인되지 않았으며, 아래 담당·검토·인계 항목은 이번 요청에 맞춰 추가한 제안이다. 사람별 담당과 검토자는 각 Issue에서 정한다.

## 먼저 확인할 Git Flow 명령

일반 기능·설정 작업은 **`develop` → `feature/<이슈번호>-<설명>` → `develop` 대상 PR·리뷰·병합** 순서로 진행한다. 사용자의 2026-10-08 지정에 따라 신규 작업 브랜치 접두사를 `feature/`로 통일한다. 기존 개발 진행 절차의 원격 기본 브랜치 기준을 이 저장소의 일반 개발 작업에서는 `develop` 기준으로 적용한다.

[참고 자료](https://gist.github.com/ihoneymon/a28138ee5309c73e94f9)의 `master` 역할은 이 저장소의 **`main`**이 맡는다. `main`으로 이름을 유지하며, GitHub의 `default` 표시는 새 기능 브랜치의 시작점을 뜻하지 않는다.

| 브랜치 | 역할 | 시작 기준 | 작업 반영 대상 |
| --- | --- | --- | --- |
| `main` | 검증된 배포 버전의 기준 | 유지하는 기본 브랜치 | release·hotfix 결과를 PR로 받음 |
| `develop` | 다음 버전의 개발 결과 통합 | 유지하는 개발 브랜치 | feature 결과와 release·hotfix 수정사항을 PR로 받음 |
| `feature/<이슈번호>-<설명>` | 기능·설정 등 작업 Issue 수행 | 최신 `develop` | `develop` |
| `release/<버전>` | 배포할 범위를 확정하고 안정화 | 최신 `develop` | `main`, 이어서 `develop`에 수정사항 반영 |
| `hotfix/<버전>` | 이미 배포한 버전의 긴급 오류 수정 | 대상 배포가 반영된 `main` | `main`, `develop`; 진행 중인 release에도 수정 반영 |

`release/0.1.0`·`hotfix/0.1.1`은 이름 예시다. 실제 버전과 대상 Issue는 배포 작업에서 정한다. `main` 병합만으로 자동 배포가 구성되는 것은 아니며, 배포 자동화와 서버 구성은 별도 범위다. 브랜치의 시작점·반영 방향은 [Git Flow 원문](https://nvie.com/posts/a-successful-git-branching-model/)을 기준으로 한다.

명령은 PowerShell과 **git-flow-next 2.0.0** 기준이다. 아래 단계는 해당 작업을 진행할 때 실행하며, 명령을 문서에 적었다는 사실이 초기화·브랜치 생성·푸시를 완료했다는 뜻은 아니다.

### 1) 도구 설치와 최초 설정

```powershell
# git flow version으로 설치 여부를 확인하고, 없을 때 설치한다.
winget install --id GitTower.GitFlowNext --exact --version 2.0.0 --source winget --scope user

# 설치 후 새 PowerShell을 열어 확인한다.
git flow version
Set-Location C:\amr-control-system-demo
git status --short
git branch -vv
git config --local --get-regexp '^gitflow\.'
```

아래 초기화는 `main`·`develop`이 존재하고 기존 Git Flow 설정과 `.gitflow` 파일이 없음을 확인한 저장소에서 한 번만 실행한다. 기존 설정이 있으면 내용을 대조해 필요한 항목만 보완하며 강제로 덮어쓰지 않는다.

```powershell
git flow init --preset=classic --defaults --main=main --develop=develop --feature=feature/ --release=release/ --hotfix=hotfix/ --shared --no-create-branches
git flow config edit topic feature --downstream-strategy=merge --shared
git flow config status
git flow config list
```

- `init`: main·develop과 브랜치 종류를 설정한다. `--no-create-branches`는 이 단계에서 새 브랜치를 만들지 않는다.
- `--feature=feature/`: `git flow feature start 7-broker-check`가 `feature/7-broker-check`를 만들도록 지정한다. 명령의 작업 이름에는 접두사를 다시 붙이지 않는다.
- `--release=release/`, `--hotfix=hotfix/`: 배포 준비·긴급수정 브랜치의 접두사를 지정한다.
- `--shared`: 팀이 검토·공유할 `.gitflow`를 만들고 이 복제본의 `.git/config`에도 적용한다. 생성된 `.gitflow`는 협업 설정 PR의 추적 대상이다.
- `--downstream-strategy=merge`: feature에 기준 브랜치 변경을 가져올 때 기본 전략을 merge로 설정한다. 이 설정 명령 자체는 병합을 실행하지 않는다.

팀원이 `.gitflow`가 포함된 저장소를 받은 뒤에는 파일 내용을 확인하고 `git flow config sync`로 자신의 로컬 설정에 적용한다. `git flow config status`로 두 설정이 일치하는지 확인한다.

이미 `.gitflow`가 있고 feature 접두사만 이전 `codex/` 규칙으로 설정되어 있다면, 설정 변경 작업에서 `git flow config edit topic feature --prefix=feature/ --shared`로 보완한다. 이 명령은 설정을 바꾸며 기존 브랜치 이름을 변경하지 않는다. `.gitflow`가 없는 미초기화 저장소에서는 위 최초 설정 절차를 사용한다.

### 2) 새 작업 Issue 시작

예시는 실제 작업 Issue [#7](https://github.com/inhhlee/amr-control-system-demo/issues/7)의 브랜치를 아직 만들지 않았고, 선행 협업 설정이 `develop`에 반영된 뒤 실행하는 명령이다. 기존 작업 브랜치·PR이 있으면 그것을 이어서 사용한다. 미커밋 변경은 소유한 작업에서 먼저 처리하며 새 Issue로 옮기거나 임의로 stash·삭제하지 않는다. 각 명령이 실패하면 원인을 확인한 뒤 다음 단계로 진행한다.

```powershell
git status --short
git fetch origin
git switch develop
git pull --ff-only origin develop
git flow feature start 7-broker-check
```

`fetch`는 원격 상태를 가져오고, `switch`는 기준 브랜치로 이동한다. `pull --ff-only`는 로컬 이력을 새로 병합하지 않고 `develop`을 갱신하며, 갈라진 이력이 있으면 중단한다. 마지막 `feature start`는 갱신한 `develop`에서 작업 브랜치를 만들고 이동한다.

COM-03은 총괄 Issue #4 아래의 작업 Issue별로 진행한다. 다음은 브랜치를 만들 때 사용할 이름이며, 생성 완료를 뜻하지 않는다.

| 작업 Issue | 새 브랜치 이름 | 시작 시점 |
| --- | --- | --- |
| [#7 · A1 Broker 확인](https://github.com/inhhlee/amr-control-system-demo/issues/7) | `feature/7-broker-check` | 협업 설정 반영·선행 조건 확인 후 |
| [#8 · A2 MQTT 접속·해제](https://github.com/inhhlee/amr-control-system-demo/issues/8) | `feature/8-mqtt-connect` | #7의 완료조건·develop 반영 확인 후 |
| [#9 · A3 Ctrl+C 정상 종료](https://github.com/inhhlee/amr-control-system-demo/issues/9) | `feature/9-graceful-shutdown` | #8의 완료조건·develop 반영 확인 후 |

**전환 시 기존 작업:** `codex/4-collaboration-rules`는 이전 규칙으로 `develop`에서 만든 협업 설정 브랜치다. 이 작업은 기존 이름으로 마치고 이후 새 작업부터 `feature/`를 적용한다. 다시 `feature start 4-collaboration-rules`로 중복 생성하지 않는다. 생성 당시 명령은 `git switch -c`였으며 Git Flow로 생성한 것으로 기록하지 않는다. 새 접두사를 설정해도 기존 `codex/` 브랜치가 `feature/` 브랜치로 인식되거나 바뀌지는 않는다. 이 기존 브랜치를 게시하는 단계에서는 `git push -u origin codex/4-collaboration-rules`를 사용하고 PR base는 `develop`으로 지정한다.

### 3) 커밋·푸시·PR 단계

구현·시험과 변경 검토를 마치고 사용자가 해당 단계를 요청했을 때 실행한다. 검토한 파일만 스테이징한 뒤 커밋하고, 최초 원격 게시에는 `publish`를 사용한다.

```powershell
git diff --cached
git commit -m "chore: 전용 Broker 기동과 관리 절차 확인 (#7)"
git flow feature publish 7-broker-check
```

`publish`는 작업 브랜치를 원격에 푸시하고 추적 관계를 설정한다. 이후 GitHub에서 **base: `develop` / compare: `feature/7-broker-check`**로 PR을 만든다. PR 생성은 `publish`가 대신 수행하지 않는다. 최초 게시 뒤 같은 PR에 추가 커밋을 반영할 때는 시험·변경 검토 후 `git push`를 사용한다.

참고 자료의 기능 마무리에 해당하는 `git flow feature finish 7-broker-check`는 로컬에서 병합·브랜치 정리를 수행한다. 이 저장소는 **PR 리뷰·병합으로 작업을 반영하므로 `finish`를 병합 절차에 함께 실행하지 않는다.** PR을 병합한 뒤 다시 `finish`할 필요도 없다. 작업 이력을 남기도록 PR은 merge commit 방식의 병합을 기본으로 하고, 반영 결과 확인 후 요청된 정리 단계에서 작업 브랜치를 정리한다.

### 4) release·hotfix는 배포 작업에서 사용

일반 COM-03 작업은 위 feature 절차를 사용한다. 다음은 배포 범위·버전·담당자가 정해졌을 때의 명령 예시이며, 현재 작업에서 실행할 명령은 아니다. 작업 트리가 깨끗하고 같은 배포 작업의 기존 브랜치·PR이 없는지 확인한 뒤 각 명령을 순서대로 수행한다.

```powershell
# 배포 준비 예시: develop에서 release/0.1.0 시작
git fetch origin
git switch develop
git pull --ff-only origin develop
git flow release start 0.1.0
# 안정화·검증·커밋을 마친 뒤 원격 게시
git flow release publish 0.1.0
```

release에서는 배포에 필요한 오류 수정·버전·문서를 정리하며 새 기능을 추가하지 않는다. `release/0.1.0` → `main` PR을 리뷰·병합하고, 검증한 main의 반영 커밋에 배포 태그를 붙인다. 이어 같은 release의 수정사항을 `develop` 대상 PR로 반영한다. 두 대상의 반영을 확인할 때까지 release 브랜치를 보존한다. 태그 생성·게시와 실제 배포는 요청된 단계에서 진행한다.

```powershell
# 배포 후 긴급수정 예시: main에서 hotfix/0.1.1 시작
git fetch origin
git switch main
git pull --ff-only origin main
git flow hotfix start 0.1.1
# 수정·검증·커밋을 마친 뒤 원격 게시
git flow hotfix publish 0.1.1
```

hotfix는 `main`이 실제 수정 대상 배포 버전을 가리키는지 먼저 확인한다. `hotfix/0.1.1` → `main` PR로 수정하고 검증한 반영 커밋에 배포 태그를 붙인다. 같은 수정사항을 `develop`에도 PR로 반영하고, 진행 중인 release가 있다면 그 release에도 반영하여 다음 배포에서 수정이 누락되지 않게 한다. 필요한 대상에 모두 반영한 뒤 브랜치를 정리한다.

release·hotfix도 PR을 통해 반영하므로 `git flow release finish`·`git flow hotfix finish`를 함께 실행하지 않는다. 여러 기준 브랜치에 반영하는 PR은 같은 배포·긴급수정 Issue에 연결하고 대상별 검증 결과를 남긴다.

명령 근거: [git-flow-next 명령](https://git-flow.sh/docs/commands/), [공유 설정](https://git-flow.sh/docs/configuration/), [Windows 설치](https://github.com/gittower/git-flow-next#installation).

## 1. 기준과 관리 위치

| 내용 | 관리 위치·사용 방법 |
| --- | --- |
| 구현·공유 설정·시험·실행 증거 | 이 저장소. 기본 작업 위치는 `C:\amr-control-system-demo` |
| 실행 방법·SDK·패키지 | [README.md](README.md), [global.json](global.json), 실제 프로젝트 파일 |
| 에이전트 작업 규칙 | [AGENTS.md](AGENTS.md). 작업별 원본 스킬과 문서 접근 방법도 여기서 확인 |
| 계획·명세·결정사항·업무 기록 | `C:\process-plan`의 담당 원본 문서. 이 저장소에 복제하지 않음 |
| 개발 단계와 요청 범위 | 「개발 진행 절차」 v0.6, 2절 작업 분할·3-2절 협업 규칙·4절 구현/PR·5절 동시 작업. 로컬 원본: `C:\process-plan\기준\개발 진행 절차.md` |

문서를 전달할 때 실제 접근 가능한 URL 또는 문서명·버전·관련 ID를 적는다. 다른 PC에서 열 수 없는 로컬 경로를 GitHub의 웹 링크로 사용하지 않는다. 접근하지 못한 자료와 적용 기준의 차이는 착수 전에 기록한다. 문서 읽기, 프로젝트 폴더 등록, 쓰기 권한은 각각 확인한다.

## 2. 담당과 인계

| 역할 | 맡을 일 |
| --- | --- |
| 구현 담당 | Issue 범위·선행 작업·변경 파일을 확인하고 구현·시험·증거·PR 설명을 준비 |
| 검토 담당 | 변경 목적, 완료조건 누락, 오류 가능성, 시험 근거와 미검증 범위를 확인 |
| 병합 담당 | 요청된 병합 단계에서 필수 승인·검사·충돌과 남은 지적을 확인하고 반영 결과를 기록 |

역할의 실제 이름은 Issue·PR에서 정한다. 1인 실습이면 본인이 수행한 검토와 AI 보조 검토를 구분해서 적으며 이를 다른 사람의 승인 리뷰로 표시하지 않는다. 이 안내 자체가 GitHub의 필수 승인·브랜치 보호 설정을 변경하지는 않는다.

작업을 넘길 때는 **Issue/PR URL, 브랜치와 코드 버전, 끝낸 범위, 실제 시험·증거, 남은 문제와 다음 행동**을 함께 전달한다. 로컬 미커밋 변경이 있으면 파일과 상태도 알린다.

## 3. Issue를 나누는 기준

일반 feature 작업의 기본 단위는 **구현·필요한 시험·PR 검토를 마칠 수 있는 작업 Issue 하나 → 작업 브랜치 하나 → PR 하나**다. release·hotfix는 같은 수정사항을 여러 기준 브랜치에 반영해야 하므로 대상별 PR을 같은 Issue에 연결한다. 파일 수나 인원수보다 확인 가능한 결과와 선행 조건을 기준으로 나눈다. 각 작업 Issue에는 다음 항목을 적는다.

| 항목 | 작성할 내용 |
| --- | --- |
| 목적·관련 근거 | 끝나면 확인할 결과, 기능 ID, 적용 문서명·버전·항목 |
| 담당·검토 | 실제 담당자와 검토자. 정하지 않았으면 미정과 지정할 시점 |
| 포함·제외 범위 | 이번에 변경할 파일·동작, 다른 Issue가 맡을 부분. 기존/생성 예정 경로 구분 |
| 선행 조건 | 필요한 Issue·PR과 반영 상태, 준비할 환경·결정 |
| 완료조건 | 관찰 가능한 결과를 체크리스트로 작성 |
| 시험 | 입력·조건·예상 결과·증거 위치. 실제 결과는 수행 후 기록 |
| 인계·후속 | 남은 항목, 다음 작업과 연결할 Issue·PR |

큰 Issue를 별도 작업 Issue로 나누기로 정했다면 다음을 적용한다.

1. 같은 범위의 기존 Issue·브랜치·PR을 먼저 찾아 중복을 피한다. 새 번호는 실제 게시 결과로 기록한다.
2. 기존 Issue를 총괄용으로 남기는 경우, 원래 완료조건을 보존하고 각 조건이 어느 작업 Issue의 결과로 충족되는지 연결한다. 총괄용이라는 역할을 본문에 명시한다.
3. 실제 구현은 나뉜 작업 Issue별 브랜치·PR에서 진행한다. 총괄 Issue에 같은 구현을 중복하는 브랜치·PR을 추가하지 않는다.
4. 선행 코드가 필요한 작업은 선행 PR 반영과 증거를 확인한 뒤 시작한다. 계획 문서의 단계 번호와 GitHub Issue 번호는 구분한다.
5. 개별 PR은 해당 작업 Issue의 완료조건을 충족할 때만 그 Issue를 닫는다. 총괄 Issue는 원래 완료조건 전체와 최종 코드에서의 증거 유효성을 확인한 뒤 종료한다.

구체적인 분할안·현재 진도는 `C:\process-plan`의 해당 과제계획·Issue 원본에서 관리한다. 같은 Issue 안의 학습 체크리스트만 나눈 경우에는 단계마다 새 Issue·브랜치·PR을 만들 필요가 없다.

## 4. 브랜치와 변경 관리

- 변경 전에 Git 상태·현재 브랜치·origin·기존 사용자 변경을 확인한다. 대상 원격은 위 실습 저장소다.
- 같은 Issue의 기존 작업 브랜치·PR을 우선 사용한다. 새 기능·설정 브랜치는 최신 `origin/develop`을 확인하고 로컬 `develop`을 갱신한 뒤 `git flow feature start <실제 이슈번호>-<짧은-영문-설명>`으로 만든다. 이름은 `feature/<실제 이슈번호>-<짧은-영문-설명>`이다. release·hotfix는 위 브랜치 역할과 배포 절차를 따른다.
- `main`과 `develop`에 직접 커밋하지 않는다. 한 작업 브랜치에는 해당 Issue 범위만 담고 다른 기능을 미리 구현하지 않는다.
- 커밋 메시지는 `<타입>: <한글 요약> (#이슈번호)`로 작성한다. 타입은 `feat`, `fix`, `docs`, `test`, `refactor`, `chore` 중 목적에 맞게 선택한다.
- 커밋 전 작업 트리와 스테이징 차이를 확인한다. 사용자 변경을 임의로 되돌리거나 함께 커밋하지 않으며 강제 푸시를 하지 않는다.
- 기본 작업 위치를 유지한다. 동시 작업을 계획하면 공유 파일·데이터 계약·선행 조건·병합 순서를 먼저 정한다. 같은 체크아웃에서 서로 다른 브랜치 작업을 동시에 수행하지 않는다. 별도 작업 디렉터리나 워크트리 사용은 사용자 요청에 따른다.
- 충돌은 양쪽 변경의 목적과 완료조건을 확인해 해결하고 영향받은 동작을 다시 확인한다.

## 5. 시험과 증거

시험은 해당 Issue의 완료조건에 맞춰 수행한다. 결과에는 코드 버전·미커밋 변경 포함 여부, 환경·적용 설정, 입력, 예상, 실제 결과, 판정과 증거 위치를 연결한다. 실행 방법은 실제 코드에 맞춰 README를 갱신한다.

| 확인 종류 | 완료로 표시할 수 있는 범위 |
| --- | --- |
| 문서·링크·설정 검사 | 검사한 문서·설정의 유효성 |
| 복원·빌드·시험 탐색 | 개발 구성의 해당 확인 범위. 시험 0건은 기능 통과가 아님 |
| 자동 시험·시험 대역 | 실행한 사례와 대역이 재현한 동작 |
| 실제 MQTT·GUI 연동 | 실제 연결한 환경과 수행한 시나리오 |

검토할 실행 증거는 `evidence/<기능 ID>/<실행ID>/`에 보관한다. 재시험에는 새 실행 ID를 사용하고 이전 실패 기록을 보존한다. 미실행 항목에는 미검증·사유·재개 조건을 적는다. 다른 저장소의 과거 통과 결과를 이번 실행 결과로 쓰지 않는다.

환경 확인만 요청받았다면 서비스·컨테이너를 시작하거나 중단하지 않는다. 실제 시험에서는 사용하는 Broker의 관리 위치·프로젝트·포트를 확인하고 다른 작업의 Broker·데이터를 임의로 중단·초기화하지 않는다.

## 6. PR·리뷰·병합

[PR 양식](.github/pull_request_template.md)에 관련 Issue, 변경 파일과 이유, 실제 시험 결과·증거, 완료조건, 미검증 항목과 남은 작업을 적는다. 증거로 확인한 항목만 체크한다.

- 일반 feature 작업 PR의 base는 `develop`이고, 본문에는 `관련 Issue: #번호`를 적는다. release·hotfix PR은 위 반영 대상에 맞춰 PR 양식의 base도 수정한다. GitHub 기본 브랜치가 `main`인 동안에는 `develop` 대상 PR의 `Closes #번호`가 자동 종료로 처리되지 않는다. 전체 완료조건과 `develop` 병합 결과를 확인한 뒤 요청된 종료 단계에서 Issue를 직접 닫는다. 기본 브랜치 대상 PR에서 `Closes #번호`를 사용할 때도 전체 완료조건 충족이 전제다. release·hotfix에서 다른 대상에 반영할 작업이 남았다면 main PR에도 종료 키워드를 넣지 않는다. 부분 작업은 관련 Issue와 남은 범위를 적는다. [GitHub의 PR·Issue 연결 기준](https://docs.github.com/en/issues/tracking-your-work-with-issues/using-issues/linking-a-pull-request-to-an-issue)
- 브랜치의 작업 이력을 남기기 위해 PR은 merge commit 방식의 병합을 기본으로 한다. 공유된 브랜치 이력을 rebase로 재작성하거나 강제 푸시하지 않는다.
- 리뷰 지적에는 위치·발생 조건·영향·수정 방향을 적는다. 수정 뒤에는 영향받는 확인 결과를 같은 PR에 연결한다.
- 병합 직전에는 필수 승인·검사·충돌·미해결 지적을 다시 확인한다. 검사 미구성·미실행을 통과로 표시하지 않는다.
- 커밋·푸시·PR 생성·리뷰·병합은 사용자가 요청한 단계까지 진행한다. 병합 뒤 실제 반영 커밋과 관련 Issue의 상태를 확인한다.

## 7. 추적 파일과 인증정보

소스·프로젝트 정의·공유 설정 예시·시험 입력·검토할 증거는 추적한다. 생성물·개인 설정·로컬 인증정보는 [.gitignore](.gitignore)로 제외한다. `evidence/`나 `*.log`를 일괄 제외하지 않는다.

제외 규칙을 바꾸면 `git check-ignore`로 생성물은 제외되고 필요한 파일은 남는지 확인한다. 실제 토큰·비밀번호를 코드·예시·로그·PR 본문에 넣지 않는다.

## 8. 변경 이력

| 일자 | 버전 | 변경 내용·근거 |
| --- | --- | --- |
| 2026-10-08 | v0.1 초안 | 사용자 요청으로 기존 협업 기준을 팀원용 안내로 연결. 담당·검토·인계 항목과 상위/작업 Issue로 나눌 때의 관리 방식을 제안. 전용 빈 양식은 없어 이 문서를 새로 작성 |
| 2026-10-08 | v0.2 초안 | 사용자 지정에 따라 develop 기반 Git Flow와 codex/ 접두사, 명령 설명, develop 대상 PR 및 Issue 종료 기준 반영. 원본 개발 진행 절차의 기본 브랜치 기준과 차이를 명시 |
| 2026-10-08 | v0.3 초안 | 사용자가 제공한 Git Flow 자료와 feature/ 선택을 반영. main·develop·feature·release·hotfix 역할, 시작·반영 방향과 명령 설명 보완. 기존 codex/4 브랜치는 유지하며 PR 병합과 로컬 finish의 차이를 명시 |
