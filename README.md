# Watermark Studio

C# / WPF 기반의 Windows용 이미지 워터마크 편집기입니다. 사진에 텍스트와 로고를 배치하고 여러 이미지에 같은 설정을 한 번에 적용합니다. 이미지 처리는 내 PC에서 이루어지며 원본은 덮어쓰지 않습니다.

## 주요 기능

- **여러 이미지 일괄 처리**: 파일 선택 또는 드래그 앤 드롭으로 추가하고 썸네일로 미리보기를 전환합니다.
- **텍스트·로고 레이어**: 여러 워터마크를 함께 사용하고 글꼴, 색상, 그림자, 배경, 불투명도를 조정합니다.
- **미리보기 직접 편집**: 마우스로 이동·크기 조정·회전하고 텍스트를 더블클릭해 수정합니다.
- **배치 도구**: 9방향 정렬, 반복 배치(모자이크), 원본 보기를 지원합니다. 이미지 크기가 달라도 위치와 크기는 비율에 맞게 적용됩니다.
- **새 파일로 저장**: 원본 형식 유지 또는 PNG·JPG 변환을 지원하며 기존 파일명과 충돌하면 번호를 붙입니다.

## 빠른 시작

### 소스에서 설치

Windows에서 소스 폴더의 `install.bat`를 더블클릭합니다. PowerShell에서는 다음과 같이 실행합니다.

```powershell
.\install.bat
```

1. 정식 **.NET 10 SDK**를 찾고, 없으면 Microsoft 공식 설치 스크립트로 자동 설치합니다.
2. 현재 Windows 아키텍처에 맞춰 Release 빌드와 런타임 포함 배포본을 만듭니다.
3. 현재 사용자에게 앱을 설치하고 바탕 화면과 시작 메뉴에 바로가기를 만듭니다.

관리자 권한이나 시스템 PATH 변경은 필요하지 않습니다. 첫 빌드 및 SDK 다운로드에는 인터넷 연결이 필요합니다. 설치 후 바로가기에서 앱을 실행하세요. 설치 스크립트가 앱을 자동 실행하지는 않습니다.

| 항목 | 위치 |
| --- | --- |
| 앱 설치 폴더 | `%LOCALAPPDATA%\Programs\WatermarkStudio` |
| 자동 설치한 SDK | `%LOCALAPPDATA%\WatermarkStudio\dotnet` |
| 배포본 | `dist/win-x64`, `dist/win-arm64` 또는 `dist/win-x86` |

기존 설치본을 실행 중이면 종료 후 다시 설치하세요. 배치 파일은 오류 메시지를 표시하고 중단하며, 터미널·자동화에서는 `.\install.bat --no-pause`로 마지막 키 입력 대기를 생략할 수 있습니다.

### 배포본 만들기 및 실행

```powershell
.\build.bat --no-pause
```

빌드만 수행하며 앱을 설치하지 않습니다. 현재 Windows 아키텍처에 해당하는 `dist` 하위 폴더를 **통째로** 전달하세요. 런타임이 포함되어 있어 받는 PC는 SDK나 .NET을 따로 설치할 필요가 없습니다.

받는 PC에서는 같은 아키텍처의 Windows에서 폴더 안의 `WatermarkStudio.exe`를 직접 실행하거나, `install.bat`로 설치합니다. 배포본의 설치에는 소스 빌드가 필요하지 않습니다.

## 앱 사용 방법

상단 **사용 방법** 버튼 또는 **F1**을 누르면 프로그램 안에서 전체 안내를 볼 수 있습니다.

1. **여러 이미지 선택**을 누르거나 이미지 파일을 화면으로 끌어다 놓습니다. **＋ 이미지 추가**로 파일을 더 넣을 수 있습니다.
2. **텍스트 추가** 또는 **로고 추가**를 누릅니다. 로고는 투명 배경의 PNG를 권장합니다.
3. 미리보기에서 워터마크를 드래그해 이동하고, 네 모서리 사각형으로 크기, 위쪽 원으로 회전을 조정합니다.
4. 오른쪽 패널에서 글꼴·색상·불투명도·위치·반복 배치를 설정합니다. 색상은 `#FFFFFF` 형식으로 입력합니다.
5. 아래쪽 썸네일을 눌러 다른 이미지에도 설정이 어울리는지 확인합니다. **원본 보기**로 적용 전 모습을 비교할 수 있습니다.
6. 저장 형식을 고르고 **워터마크 적용 후 저장**을 누른 뒤 출력 폴더를 선택합니다. 목록의 모든 이미지에 현재 설정을 적용합니다.

### 마우스·키보드 조작

| 조작 | 동작 |
| --- | --- |
| 워터마크 안쪽 드래그 | 위치 이동 |
| 모서리 사각형 드래그 | 크기 조정, 로고 가로세로 비율 유지 |
| 위쪽 원 드래그 | 회전 |
| Shift를 누른 채 회전 | 15도 단위로 회전 |
| 텍스트 더블클릭 또는 선택 후 F2 | 미리보기에서 텍스트 편집 |
| 직접 편집 중 Enter / Shift+Enter / Esc | 편집 완료 / 줄바꿈 / 이번 편집 취소 |
| F1 | 사용 방법 열기 |

### 저장 결과

출력 파일명은 `사진_watermarked.png` 형태이며, 같은 이름이 있으면 `사진_watermarked (1).png`처럼 번호가 붙습니다. 원본 파일과 기존 출력 파일은 유지합니다.

**작업 중지**를 누르면 현재 파일의 저장을 마친 뒤 중지하고 이미 저장한 결과는 유지합니다. 일부 파일이 실패해도 나머지는 계속 처리하며 완료 후 저장 개수와 실패 내역을 표시합니다.

## 지원 형식 및 제한 사항

| 항목 | 지원 내용 |
| --- | --- |
| 입력 이미지·로고 | JPG / JPEG, PNG, 정적 GIF |
| 출력 형식 | 원본 형식 유지, PNG, JPG |
| 투명도 | PNG는 유지, JPG는 투명 부분을 흰색으로 저장 |
| 이미지 크기 | 6,000만 픽셀 이하이면서 한 변 32,767px 이하 |
| 출력 크기 | EXIF 방향을 반영한 이미지의 픽셀 크기 유지 |

- 움직이는 GIF는 지원하지 않습니다. GIF 출력은 색상 수가 제한됩니다.
- EXIF 방향은 읽을 때 반영하지만 출력 파일에 EXIF 등 원본 메타데이터를 보존하지 않습니다.
- 편집 설정과 이미지 목록은 앱 종료 시 초기화됩니다. 종료 전에 결과를 저장하세요.

## 개발 및 검증

Windows와 정식 **.NET 10 SDK**가 필요합니다. 앱은 `net10.0-windows` / WPF를 사용합니다. 아래 명령은 저장소 루트에서 실행합니다.

### 개발 실행

```powershell
dotnet run --project src/WatermarkStudio/WatermarkStudio.csproj
```

자동 설치된 SDK만 있다면 `dotnet` 대신 `& "$env:LOCALAPPDATA\WatermarkStudio\dotnet\dotnet.exe"`를 사용하세요.

### Visual Studio에서 열기

**Visual Studio 2026(18.0 이상)**과 **.NET 데스크톱 개발** 워크로드가 필요합니다. .NET 10은 VS2022의 지원 대상이 아닙니다. [Microsoft 공식 지원 기준](https://learn.microsoft.com/en-us/dotnet/core/install/windows#net-versions-and-visual-studio)을 참고하세요.

1. 시작 메뉴에서 **Visual Studio 2026**을 실행합니다.
2. **파일 → 열기 → 프로젝트/솔루션**에서 `WatermarkStudio.sln`을 선택합니다.
3. 솔루션 탐색기에서 **WatermarkStudio**를 시작 프로젝트로 설정하고 F5로 실행합니다.

VS2022에서 만든 솔루션도 VS2026에서 열어 개발할 수 있습니다. 이 솔루션의 버전 정보와 최소 IDE 버전은 VS2026 기준으로 설정되어 있습니다.

`현재 Visual Studio 버전은 .NET 10.0 타겟팅을 지원하지 않습니다` 오류가 나오면 현재 솔루션을 닫고 **VS2026에서 다시 여세요**. VS2026 설치 후에도 이미 실행 중인 VS2022는 그대로 유지됩니다. 솔루션을 더블클릭했을 때 VS2022가 열리는 환경에서도 위 절차로 VS2026을 직접 실행하면 됩니다. **도움말 → Microsoft Visual Studio 정보**에서 실행 중인 IDE 버전이 18.x인지 확인할 수 있습니다.

### 동작 검증

```powershell
dotnet run --project tests/WatermarkStudio.Checks/WatermarkStudio.Checks.csproj "-p:OutDir=$PWD/.artifacts/test-check/checks/" -- .artifacts/test-check/fixtures
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Setup.Checks.ps1
```

첫 명령은 별도 테스트 실행기로 렌더링, 투명도, 회전·크기 조정, 텍스트 직접 편집, 파일 저장, 일괄 처리와 취소 등을 확인합니다. 두 번째 명령은 SDK 탐색·설치 분기, 배포·설치, 바로가기, 한글·공백 경로와 콘솔 인코딩을 모의 환경에서 확인하며 실제 SDK를 다운로드하거나 설치하지 않습니다.

검증 결과와 픽스처는 `.artifacts/test-check`에 유지합니다. `build.bat`의 중간 출력도 이 폴더를 사용합니다. 배치 파일과 설치 스크립트는 BOM 없는 ASCII이며, 실행 중 콘솔과 PowerShell 외부 프로그램 입출력을 UTF-8로 맞추고 종료 시 이전 설정을 복원합니다.

## 프로젝트 구조

```text
WatermarkStudio.sln              솔루션
src/WatermarkStudio/
  EditorView.xaml[.cs]           편집 화면과 파일 추가·저장 흐름
  EditorState.cs                이미지 목록과 워터마크 레이어 상태
  WatermarkCanvas.cs            미리보기와 텍스트 직접 편집
  LayerGesture.cs               이동·크기 조정·회전 처리
  WatermarkRenderer.cs          미리보기·출력 공통 렌더링
  ImageFiles.cs                 이미지 읽기와 일괄 저장
  HelpWindow.xaml[.cs]           앱 내 사용 안내
  Assets/                       앱 아이콘
scripts/setup.ps1               SDK 준비, 배포본 빌드 및 사용자 설치
tests/WatermarkStudio.Checks/    앱 동작 검증 실행기
tests/Setup.Checks.ps1           빌드·설치 스크립트 검증
build.bat / install.bat          Windows 실행 진입점
```

## 참고 자료

- 기능 참고: [iLoveIMG 워터마크](https://www.iloveimg.com/ko/watermark-image)
- 참고 캡처 위치: `assets/reference`
- 아이콘: `src/WatermarkStudio/Assets/app-icon.png`, `app.ico` — 내장 이미지 생성 도구 사용. [생성 프롬프트](assets/icon-prompt.txt).
