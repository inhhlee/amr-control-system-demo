using Xunit;

// 이 프로젝트의 화면 시험은 같은 데스크톱에서 실제 창/모달 메시지 루프를 사용한다.
// 서로 다른 시험의 창 조작을 겹치지 않게 한다. 각 시험 내부의 명시적 동시성 검증은 유지한다.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
