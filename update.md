# ExCord 업데이트 기록

이 문서는 버전별 변경 사항을 **릴리스 노트 형식**으로 기록합니다.
앞으로 신규 버전도 아래 템플릿(Added / Changed / Fixed / Removed)에 맞춰 작성합니다.

---

## v2.0.0

WebView 기능을 단일 오버레이 편집에서 **다중 뷰어 관리 구조**로 확장한 메이저 업데이트입니다.

### Added
- WebView 탭을 단일 폼에서 **뷰어별 접이식 카드(Expander) 목록 UI**로 확장.
- 하단 고정 **`+ Add Viewer / + 새 뷰어 추가`** 버튼 추가.
  - 기본 생성값: `Overlay N`, 빈 URL, `300×300`, 기본 위치.
- 뷰어별 개별 설정 지원:
  - 이름, 활성화(On/Off), URL 적용, 편집 모드, X/Y/Width/Height
  - 투명화, 상호작용 모드, Shift+우클릭 편집 진입
- 카드 확장/축소 상태를 뷰어별로 유지하고, 확장 상태에서 이름 인라인 편집(Enter/Esc 포커스 이동) 지원.
- 좌측 ☰ 핸들 기반 드래그 정렬 추가.
  - 드래그 중 반투명 이름 팝업 표시
  - 실시간 재배열 및 160ms 이징 애니메이션
  - 순서 변경 시 즉시 저장
- 헤더 우클릭 컨텍스트 메뉴로 뷰어 삭제 기능 추가.
- 카드/스위치/컨텍스트 메뉴/삭제 항목 전용 스타일 추가.
- 기본 스크롤바를 대체하는 2px 커스텀 스크롤바 추가(스크롤 가능 시 표시, ScrollViewer 동기화).
- 설정창이 전체 뷰어 창의 편집 모드/종료/좌표 변경 이벤트를 구독하도록 확장.
  - 오버레이 직접 이동/리사이즈 시 X/Y/Width/Height 즉시 반영 및 저장
  - 설정창 종료 또는 WebView 전체 비활성화 시 편집 중 항목 저장 후 종료
- 다중 뷰어 연동 API 추가:
  - `OverlayManager.GetWindows()`
  - `SetOverlayWindows(IReadOnlyDictionary<string, WebViewOverlayWindow>?)`
  - `App.xaml.cs` 호출부 연동
- `OverlayItemSettings`에 뷰어별 옵션 3종 추가:
  - `TransparencyEnabled` (기본 OFF)
  - `InteractionEnabled` (기본 ON)
  - `ShiftRightClickEditEnabled` (기본 ON)
- 로컬라이제이션 문자열 4종(영문/국문) 추가:
  - `OverlayTransparency`, `OverlayInteractionMode`, `OverlayShiftRightClickEdit`, `AddOverlay`

### Changed
- WebView 탭의 단일 URL/좌표 입력 구조를 목록형 다중 뷰어 편집 구조로 교체.
- 이름/URL/좌표를 `OverlayItemSettings`에 직접 바인딩(`UpdateSourceTrigger=PropertyChanged`)하도록 변경.
- 설정 저장 흐름 전환:
  - v1.1.0: 저장 시 레거시 필드를 첫 번째 오버레이로 덮어씀
  - v2.0.0: 첫 번째 오버레이 값을 레거시 필드로 미러링
- 레거시 호환 유지:
  - 레거시 필드는 계속 기록
  - 로드 시 레거시 → `OverlayItems` 마이그레이션 유지
  - 목록 첫 번째 뷰어가 대표값 역할(순서 변경 시 영향)
- `InteractionEnabled`가 켜진 경우 `TransparencyEnabled`를 사용할 수 없도록 모델/정규화에서 강제하고, UI에서도 비활성화 처리.
- WebView2 기본 컨텍스트 메뉴(`AreDefaultContextMenusEnabled`)를 상호작용 모드에서만 활성화.
- 상호작용 모드 상태 적용 경로를 `ApplyInteractionState()`로 통합하고, `상호작용 OFF + 투명화 OFF` 조합의 동작을 순수 표시용(클릭 통과)으로 정책화.
- 신규/기본 오버레이 초기값을 `상호작용 ON + 투명화 OFF`로 조정.
- 앱 공통 `Button`/`ToggleButton` 템플릿의 `ContentPresenter`에 `ContentSource="Content"`를 명시.
- 오버레이 카드 내부 문자열은 `OverlayLocalizedElement_Loaded`를 통해 현재 언어 기준으로 반영되며, 언어 변경 시 목록 갱신 흐름을 사용.

### Fixed
- WebView 탭 커스텀 스크롤바가 사라지거나 초기 상태에서 레이아웃 폭이 비정상적으로 줄어들던 문제 수정.
  - 얇은 스크롤바를 우측 오버레이 방식으로 분리 배치해 탭 본문 폭에 영향이 없도록 조정.
  - 초기 로드/갱신 시 스크롤바 동기화 보강으로 한 번 펼친 뒤에만 정상 폭이 되던 현상 해결.
- `Shift+우클릭 편집 진입`이 동작하지 않던 문제 수정.
  - 창 이벤트 기반 감지를 제거하고 전역 저수준 마우스 훅(`OverlayShiftRightClickHook`) 기반으로 전환.
  - 편집 진입 이벤트 순서를 조정해 설정창 토글 Checked 재진입 이슈 방지.
- 투명화 ON 시 일부 웹페이지(예: YouTube)에서 흰 배경처럼 보이던 문제 수정.
  - opacity 적용 중에만 `html/body` 배경을 투명 강제하고, opacity 복원 시 스타일을 제거하도록 변경.
- 오버레이 `상호작용 모드`가 실질 입력(마우스/키보드)으로 동작하지 않던 문제 수정.
  - 상호작용 ON 시 창/루트 배경을 알파 1 브러시로 전환해 히트테스트를 보장하고, OFF 시 완전 투명으로 복원.
  - 활성화 시 `WebView.Focus()` 적용 및 `AreBrowserAcceleratorKeysEnabled`/컨텍스트 메뉴 설정을 상호작용 상태와 동기화.
  - `ApplyInteractionState()` 경로로 타이머/opacity/클릭통과 상태를 통합 적용해 토글 즉시 반영되도록 정리.
- WebView 전체 ON/OFF(설정창 토글, 트레이 Overlay 토글)가 실제 오버레이 상태와 분리되던 문제 수정.
  - 마스터 스위치(`WebViewEnabled`)와 뷰어별 `Enabled` 동기화 결합 해제.
  - 트레이 메뉴 라벨/체크 상태 갱신을 적용 경로와 동기화.
- 첫 번째 뷰어 개별 OFF 시 다른 뷰어까지 함께 닫히던 부수 문제 수정.

### Removed
- 단일 WebView 전용 설정/동기화 코드 제거:
  - 단일 오버레이 창 참조
  - 단일 URL/좌표 입력 흐름
  - 좌표 파싱/보정 헬퍼(`TryParseWebViewGeometry`, `SetTextPreservingCaret`, `SyncWebViewFieldsFrom*` 등)
  - 좌표 저장용 300ms 디바운스 타이머(`_webViewGeometrySaveTimer`)
- 미사용 핸들러 `ResetWebViewPositionButton_Click` 제거.
- 좌표 유효성 검증을 입력 시점 중심에서 모델 정규화(`ValidateAndNormalize`) 및 저장 시점 중심으로 정리.

### Performance
- WebView 미응답 감시 타임아웃을 `10초`에서 `5초`로 단축해 실패 감지/재시도 시작 시간을 개선.
- `투명화 OFF` 또는 `상호작용 모드 ON` 뷰어는 호버 타이머에서 조기 반환하여 커서 조회/히트 테스트/투명도 계산을 생략.

---

## v1.1.0

WebView 중심 구조로 확장하기 위한 리팩터링과 UI/안정성 개선이 포함된 업데이트입니다.

### Added
- WebView 오버레이 안정성 강화를 위한 네비게이션 감시/자동 재시도 로직 추가.
- 다중 오버레이 확장 기반(`OverlayItems`) 설정 모델 도입.
- TTS 입력창 최근 입력 히스토리(↑/↓ 탐색) 추가.

### Changed
- 프로젝트 전반 용어를 `GifTalk`에서 `WebView` 중심으로 정리.
- 오버레이 뷰/클래스명 변경:
  - `GifTalkOverlayWindow` → `WebViewOverlayWindow`
- 설정창 탭 구조 개편:
  - `General` 탭을 설정 탭(⚙) 개념으로 정리
  - 설정 탭 우측 고정 배치
  - WebView 탭 우선 표시
- URL 라벨 문구 변경:
  - `GIF URL` → `Web URL`
- 언어 목록 표시를 `English / 한국어`로 고정.

### Fixed
- 자동 시작 직후 간헐적으로 WebView 로딩 화면이 고정되는 문제 수정.
- 설정에서 언어 변경 시 TTS 입력창 문구가 즉시 반영되지 않던 문제 수정.
- 언어 선택 콤보박스에 타입명이 보이던 표시 문제 수정.

### Removed
- 외부 설정 버튼 제거(탭 구조로 통합).
- WebView 오버레이 미사용 리소스 제거.
- 설정창 미사용 이벤트 핸들러(더미) 제거.

### Performance
- `OverlayManager`에서 동일 URL에 대한 불필요한 `Navigate` 재호출 방지.
- WebView 미응답 감지 타임아웃 조정:
  - `15초` → `10초`

---

## v1.0.1

소규모 수정이 포함된 패치 릴리스입니다.

### Fixed
- **전체화면에서 GifTalk 미표시 문제 수정**: 게임/영상 등 전체화면 프로그램 실행 시 GifTalk 오버레이가 다른 창 뒤로 가려지던 문제를 수정했습니다. 오버레이가 주기적으로 최상위 상태를 다시 보장하도록 개선했습니다.
- **설치 정보 수정**: 설치 과정에서 표시되던 잘못된 안내 정보를 수정했습니다.

---

## v1.0.0

초기 릴리스. ExCord는 단일 기능 중심이 아니라 Discord 사용 중 아쉬운 점을 계속 보완해 나가는 개인용 확장 도구 모음으로 시작했습니다.

### Added

#### TTS (Text-to-Speech)
- 입력한 텍스트를 음성으로 변환해 통화 상대에게 전달합니다.
- Windows OneCore 음성과 SAPI5 음성을 모두 감지해 선택할 수 있습니다.
- 합성된 음성을 가상 오디오 케이블 장치로 출력하며, 출력 장치/볼륨 설정을 지원합니다.
- 어떤 앱에서든 입력창을 띄울 수 있는 전역 단축키를 지원합니다.
- 입력 텍스트 언어를 자동 감지하고, 선호 음성이 해당 언어를 지원하지 않으면 대체 음성으로 자동 폴백합니다.

#### GifTalk (Reactive Character Overlay)
- 반응형 캐릭터를 테두리 없는 always-on-top 오버레이로 표시합니다.
- 마우스 오버 시 자동으로 페이드되며 클릭 통과 상태가 되어 하단 창 조작을 방해하지 않습니다.
- 편집 모드에서 드래그 이동/크기 조절을 지원하며, 확인/취소 워크플로를 제공합니다.
- X/Y, Width/Height 숫자 입력과 원클릭 기본 위치 초기화를 지원합니다.
- 기능을 온/오프할 수 있고, 비활성화 시 WebView 리소스를 해제해 메모리를 절약합니다.

#### General
- Windows 시작 시 자동 실행을 지원하며, 실행 경로 변경 시 시작 등록 경로를 자동 보정합니다.
- 설정창에서 버전 표시 및 업데이트 체크를 제공합니다.
- 트레이 상주 백그라운드 앱으로 동작하며, 트레이 메뉴에서 주요 기능을 빠르게 토글할 수 있습니다.

---

## 기록 템플릿 (다음 버전부터 사용)

```md
## vX.Y.Z

한 줄 요약

### Added
- 

### Changed
- 

### Fixed
- 

### Removed
- 

### Performance
- 
```
