# ExCord

**언어: [한국어](README.md) | [English](README.en.md)**

ExCord는 Discord 사용 중 자주 필요한 기능을 빠르게 쓸 수 있도록 만든 개인용 Windows 트레이 유틸리티입니다.

- 전역 단축키 기반 TTS 입력/재생
- 화면 위 WebView 오버레이 표시/편집
- 트레이 상주 + 설정 창 관리

---

## 주요 기능

### 1) TTS (Text-to-Speech)
- 전역 단축키(기본 `Ctrl + Alt + T`)로 입력창 호출
- 입력한 문장을 음성으로 합성해 선택한 출력 장치로 재생
- OneCore / SAPI5 음성 선택 지원
- 출력 장치, 볼륨, 모니터링 재생 설정 지원
- 입력창에서 최근 입력 기록(↑/↓) 탐색 지원

### 2) WebView 오버레이
- URL(GIF/이미지/웹페이지) 기반 오버레이 표시
- Always-on-top 오버레이 + 마우스 오버 시 투명/클릭 통과
- 편집 모드에서 위치/크기 조절 및 저장/취소
- 좌표(X/Y), 크기(Width/Height) 수치 입력 지원

### 3) 일반 기능
- Windows 시작 시 자동 실행
- 트레이 메뉴에서 기능 토글 및 설정 창 열기
- 앱 버전 표시 및 업데이트 체크

---

## 요구 사항

- Windows 10 (2004, 19041+) 또는 Windows 11
- .NET 8 Desktop Runtime
- WebView2 Runtime
- (Discord TTS 전달 시) Virtual Audio Cable (예: VB-CABLE)

---

## 빠른 시작

1. 앱 실행 후 트레이 아이콘 확인
2. 설정 창에서 TTS 출력 장치 및 음성 선택
3. Discord 입력 장치를 동일 가상 장치로 설정
4. 단축키로 입력창을 열어 TTS 전송
5. WebView 탭에서 URL 적용 및 오버레이 배치

---

## 설정 파일 경로

- `%AppData%\ExCord\settings.json`

---

## 업데이트 기록

- 버전별 변경 사항: [update.md](update.md)
