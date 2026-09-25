# ProjectMT UI 폰트 사용 기준

## 역할

| 역할 | 원본 폰트 | TMP Font Asset |
|---|---|---|
| 본문·상태·수치 | Spoqa Han Sans Neo Regular | `FontAssets/TMP_SpoqaHanSansNeo_Body.asset` |
| 큰 제목·전투 결과 | 학교안심 여행 Regular | `FontAssets/TMP_HakgyoansimYeohaeng_Title.asset` |
| 버튼·선택 라벨 | 눈누 기초고딕 Regular | `FontAssets/TMP_NoonnuBasicGothic_Button.asset` |

- 모바일 크기를 줄이기 위해 Spoqa Han Sans Neo는 제공 ZIP의 TTF subset을 사용한다.
- 세 Font Asset은 Dynamic Atlas와 Multi Atlas를 사용하며 현재 ProjectMT 한글 UI 문자를 미리 등록했다.
- 제목·버튼 폰트에서 없는 글자는 Spoqa Han Sans Neo 본문 폰트로 대체한다.
- 일반 본문 UI를 새로 만들 때는 Spoqa, 큰 타이틀은 학교안심 여행, 버튼 라벨은 눈누 기초고딕을 우선한다.

## 출처와 라이선스

- Spoqa Han Sans Neo: 제공 ZIP의 라이선스 원문을 `Licenses/LICENSE_SpoqaHanSansNeo.txt`에 보존했다.
- 학교안심 여행: 한국교육학술정보원(KERIS) 제작, OFL. 폰트 파일 자체 판매를 제외한 사용·수정·재배포 가능.
  - https://gongu.copyright.or.kr/gongu/bbs/B0000018/list.do?bbsSeCd=03&menuNo=200001
- 눈누 기초고딕: 프로젝트눈누 × 토끼네활자공장 제작. 개인·상업 사용, 수정·재배포와 게임 임베딩 가능하며 폰트 파일 자체의 유료 판매는 금지.
  - https://noonnu.cc/font_page/1496

릴리스 전에는 위 원 배포처의 최신 라이선스 조건과 저장소 공개 범위를 다시 확인한다.
