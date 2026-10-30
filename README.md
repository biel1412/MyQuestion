# MyQuestion · Assistente da Central de Regramento

Sistema web em **C# (Blazor WebAssembly)** para tirar dúvidas operacionais a partir de um print.
Você cola o print do Teams (ou anexa um PDF), o sistema lê o texto e indica qual regramento da
[Central de Regramento](https://konigmauricio1-dotcom.github.io/regramentos/) responde à dúvida.

**Acesso:** https://biel1412.github.io/MyQuestion/

## Como usar

1. No Teams, tire o print da dúvida (`Win + Shift + S`).
2. No MyQuestion, aperte `Ctrl + V` (ou arraste/anexe imagens e PDFs).
3. O texto é lido (OCR em português) e aparece no campo "Texto da dúvida". Dá para corrigir ou digitar.
4. O sistema mostra o **manual indicado**, outras opções, os termos que levaram à indicação e, se houver, os **trechos de regra**.
5. Use **Abrir no SharePoint** ou **Copiar resposta** para responder no Teams.

Tudo roda no navegador: as imagens e PDFs **não são enviados para nenhum servidor**.

## Como a busca funciona

- `src/MyQuestion.Core`: motor de busca em C#
  - normaliza o texto (acentos, plural, palavras sem importância);
  - procura as palavras-chave de cada manual (expressões compostas valem mais);
  - detecta pistas como CPF/CNPJ formatados, PDF citado e termos de agro para reforçar PF, PJ ou Agro;
  - ranqueia trechos de regra com BM25 e destaca os termos encontrados.
- `src/MyQuestion.Web`: interface Blazor. A leitura de imagem usa Tesseract.js e a de PDF usa pdf.js (PDF escaneado também passa por OCR).

## Base de regras: `src/MyQuestion.Web/wwwroot/data/base.json`

Os manuais da Central estão no SharePoint da empresa, que exige login, então o site não lê o conteúdo deles.
A base traz os 19 manuais com palavras-chave. Para respostas com o **texto da regra**, copie os trechos do OneNote para `regras`:

```json
"regras": [
  {
    "manual": "endereco",
    "titulo": "Comprovante em nome de terceiros",
    "texto": "Texto da regra copiado do OneNote.",
    "tags": ["terceiro", "comprovante de residência"]
  }
]
```

O campo `manual` é o `id` do manual (aparece na página **Base de regras**). Para melhorar o reconhecimento,
inclua termos em `palavrasChave`. Ao salvar no GitHub, o site é publicado de novo em cerca de 2 minutos.

## Publicação

O workflow `.github/workflows/deploy.yml` compila com .NET 8 a cada push na `main` e publica na branch `gh-pages`.
Na primeira vez, confira em **Settings → Pages** se a origem está como *Deploy from a branch* → `gh-pages` / `(root)`.

## Rodar localmente

```bash
dotnet run --project src/MyQuestion.Web
```
