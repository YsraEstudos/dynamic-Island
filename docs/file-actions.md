# Ações de arquivos na Dynamic Island

Arraste arquivos ou pastas do Explorer para a ilha, mesmo recolhida. O contorno jade indica que ela aceita o lote e as ações se abrem em setores perto do ponto de arrasto. O menu reproduz a referência **Radial Drop.html**, com centro informativo, destaque colorido e animações; não há painel de fundo atrás da roda. Solte sobre uma ação para confirmar e abrir seus ajustes, ou solte na ilha e escolha depois. Escolha o destino e clique em **Executar**. Cancelar o arrasto ou soltar fora dispensa a prévia. Escape fecha; durante uma execução, fechar solicita o cancelamento primeiro. **Guardar na bandeja** mantém o comportamento do File Tray.

Cada execução cria uma pasta `Island-AAAAMMDD-HHMMSS` no destino. Nenhum original é renomeado, substituído ou apagado. O lote é preparado em uma subpasta temporária e só disponibiliza arquivos completos após concluir; cancelamento e erros descartam a preparação. A pasta de saída pode ficar vazia nesses casos.

| Ação | Resultado / opções |
| --- | --- |
| Renomear em lote | Cópias; `{name}` nome original, `{date}` data de modificação, `{n:000}` sequência e `{content}` primeira linha de TXT/MD/CSV/JSON/LOG. Outros formatos usam o nome original para conteúdo. Colisões recebem sufixos. Pastas são percorridas e as cópias ficam reunidas na saída. |
| Encontrar duplicados | Relatório HTML de igualdade SHA-256 e candidatos visuais por hash de imagens. Não remove arquivos. A comparação visual limita-se às primeiras 1.000 imagens e 5.000 pares. |
| Criar pacote | ZIP com nome sugerido/editável e estrutura das pastas. |
| Montar índice | HTML local com caminhos, nomes, bytes, datas e miniaturas incorporadas de imagens. |
| Folha de contato | PDF A4, 12 imagens por página. |
| Imagens | Entrada PNG, JPG, WebP, HEIC/HEIF ou BMP; saída PNG, JPG ou WebP, com largura máxima e qualidade. Preserva proporção, aplica orientação e remove metadados. JPG recebe fundo branco quando há transparência. |
| Imagens em PDF | Uma imagem por página, na ordem recebida. |
| Documentos em PDF | DOCX/PPTX/XLSX por LibreOffice. Fontes e layout dependem da instalação. |
| Juntar / dividir PDFs | Junta na ordem recebida; dividir gera um arquivo por página. PDFs protegidos precisam ser desbloqueados antes. |
| Comprimir PDFs | Ghostscript com perfil ebook; pode reamostrar imagens e não garante arquivo menor. |
| Vídeos | Saída MP4/MOV/WebM, áudio MP3, GIF ou prévia JPG. Início/duração em segundos, largura e qualidade. |
| Áudios | MP3/WAV/M4A/OGG; início e duração para cortes, qualidade para bitrate dos formatos comprimidos. WAV é sem compressão. |
| Extrair ZIP | Uma pasta por ZIP; rejeita caminhos externos e links. Limite de 20.000 entradas e 10 GB por ZIP. |
| CSV → XLSX | CSV com vírgulas, aspas e células multilinha. Células são texto, sem executar fórmulas. Limites normais de linhas/colunas/células do Excel. |
| JSON → CSV | Array de objetos, união das chaves; estruturas aninhadas permanecem em JSON dentro da célula. Prefixa fórmulas potenciais para abertura segura em planilhas. |

As opções são contextuais: conversões só aparecem quando todos os arquivos selecionados são compatíveis. Pastas oferecem as quatro ações gerais; a enumeração ocorre em segundo plano. Links/junções não são percorridos. Cada lote pode conter até 20.000 arquivos; imagens têm limite de 100 megapixels. Compressão não garante redução, sobretudo em arquivos já otimizados.

## Conversores

Imagens usam [Magick.NET](https://github.com/dlemstra/Magick.NET) incorporado, e criação/união/divisão de PDF usa [PDFsharp](https://docs.pdfsharp.net/PDFsharp/Overview/Features.html) 6.2.2. O aplicativo distribuído é Windows x64.

Áudio e vídeo exigem `ffmpeg.exe` no PATH ou em `tools` ao lado do executável. Documentos exigem LibreOffice (`soffice.exe` no PATH ou na pasta padrão de Program Files). Compressão de PDF exige Ghostscript 64 bits (`gswin64c.exe` no PATH, tools ou instalação padrão em Program Files/gs). Quando falta um conversor, a interface explica qual instalar e desabilita Executar para aquela ação.

## Validação desta implementação

Testes exercitam arquivos reais: renomeação sem alterar a origem, colisões, duplicados, prevenção de escape de ZIP, pacote/extração, CSV/XLSX, JSON/CSV, união/divisão de PDFs reabertos, miniaturas, hash visual e imagem/PDF/folha de contato. Testes de mídia usam FFmpeg instalado para produzir e decodificar MP4, MOV, WebM, GIF, JPG, MP3, WAV, M4A e OGG. Uma amostra HEIC oficial de [libheif](https://github.com/strukturag/libheif/blob/master/examples/example.heic) foi decodificada para JPG; o teste opcional usa a variável `FILE_ORBIT_HEIC_SAMPLE` para um arquivo local, sem baixar material durante a suíte normal.

WPF é renderizado fora da tela para verificar a órbita e o painel; selecionar uma ação não inicia trabalho. A fluidez percebida e o arrasto OLE do Explorer ainda precisam de uso manual em monitores/DPI reais. LibreOffice e Ghostscript não estão instalados nesta máquina, portanto suas conversões não foram homologadas aqui. PDF para DOCX permanece fora do escopo solicitado.

Resultado final: `dotnet test DynamicIsland.slnx --no-restore -v minimal` passou com 1.149 testes Core e 398 Windows (1.547 no total). O teste existente `QuickNotesViewModelTests.Autosave_persists_after_the_quiet_period` falhou por temporização em uma execução intermediária e passou na execução final completa; não foi alterado. O executável também passou no modo `--smoke-test` do projeto. A revisão independente apontou arquivos incompletos após cancelamento; o fluxo foi corrigido para preparar o lote antes de disponibilizar os resultados, com regressão que reproduziu a falha antes da correção.

Na revisão visual seguinte, o menu passou a ser transparente, sem símbolo central, e abrir durante o arrasto. Os três testes do menu verificam transparência no bitmap, raio circular, seleção sem execução, animações/redução de movimento e transição de prévia de arrasto para seleção. A suíte completa dessa revisão teve 1.149 Core e 399 Windows aprovados, com uma falha intermitente no mesmo teste de autosave; os três testes do menu e o autosave passaram na repetição direcionada. O gesto OLE em monitores reais permanece como limite de validação manual.

A última execução completa passou: 1.149 Core + 400 Windows = 1.549 testes. A revisão independente também identificou risco de seleção durante a entrada das pétalas; elas agora só recebem interação após a animação, enquanto soltar durante a abertura apenas mantém as escolhas abertas.

Na revisão compacta aprovada, o menu passou para 340 × 360 DIP, raio 110, ícones de 56 DIP e rótulos curtos. O conteúdo visual anima sem deslocar o alvo de arrasto. Os ajustes usam sua própria área de 680 × 620 DIP e Voltar restaura o tamanho compacto. A suíte dessa revisão passou com 1.149 Core + 404 Windows = 1.553 testes; os sete testes do menu cobrem geometria sem sobreposição, transparência, ícones, entrada/saída animada, redução de movimento, posicionamento e restauração. A revisão independente não encontrou falhas importantes. Arrasto OLE real e fluidez percebida em monitores/DPI diferentes continuam fora da homologação automática.

Na réplica posterior do **Radial Drop.html**, as ações passaram a ser setores arredondados com centro informativo, paleta por ação, fonte Sora incorporada, entrada com rotação e mola, salto/glow de hover e flash/partículas de confirmação. O número de fatias acompanha os arquivos selecionados; as funções demonstrativas do HTML não foram executadas nem substituem as operações reais. A suíte completa passou com 1.149 Core + 406 Windows = 1.555 testes. Nove testes do radial incluem `InputHitTest` real de WPF, isolamento do centro, limpeza de seleção ao voltar, confirmação sem executar a operação, tipografia e restauração dos limites da janela. O navegador bloqueou a abertura de `file:`; a referência foi analisada como código de desenho e a réplica foi renderizada em WPF, sem executar o JavaScript do arquivo. O arrasto do Explorer e a fluidez percebida seguem como validação manual pendente.
