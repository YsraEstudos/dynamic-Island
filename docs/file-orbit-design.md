# Órbita de arquivos — desenho e execução

Solicitação: desenhar e implementar diretamente, sem confirmação intermediária.

A ilha aceita arquivos e pastas em qualquer modo visível. Ao arrastar sobre ela, um menu circular surge no ponto de entrada, limitado à área útil do monitor. O menu é transparente: sem painel, moldura, véu, anel ou símbolo central. Somente as ações individuais têm superfície para leitura sobre o desktop. Elas se expandem a partir do centro com deslocamento, escala e entrada escalonada; hover e arrasto realçam o alvo. É possível soltar diretamente em uma ação ou soltar na ilha e escolher depois. Soltar fora ou cancelar o arrasto dispensa a prévia. Reduzir animações elimina deslocamento/escala. Escape e Fechar dispensam o menu.

Escolher uma ação abre opções explícitas, lista dos arquivos e Executar. Selecionar nunca executa. Trabalho em segundo plano, cancelamento e status mantêm a interface responsiva. Resultados vão para uma pasta nova escolhida pelo usuário; originais não são alterados. Renomeação em lote produz cópias com prévia de nomes. ZIP preserva pastas; extração rejeita caminhos externos e links. Duplicados são relatados, nunca apagados.

## Implementação

1. Núcleo `Island.Core/Files`: catálogo por tipos de arquivo, opções, operações, relatórios, ZIP, dados e PDF. Testes reais com arquivos temporários, incluindo colisões, escape de ZIP, CSV citado e PDF reaberto.
2. Conversores: Magick.NET-Q8-x64 incorporado para imagens, miniaturas e hashes visuais; FFmpeg detectado no PATH ou pasta tools para áudio e vídeo; LibreOffice para documentos; Ghostscript para compressão PDF. Dependências ausentes são informadas antes da execução. PDFsharp 6.2.2 para PDFs, com dependência criptográfica corrigida.
3. `FileOrbitWindow`: seletor radial, opções por operação, destino, progresso e cancelamento. Arrasto da ilha integrado por partial separado, preservando widgets e trabalho local.
4. Verificação: testes do núcleo, testes WPF renderizados, conversões reais FFmpeg e suíte da solução. Registrar limites de homologação.

## Limites e revisão

Relatórios HTML locais incluem miniaturas incorporadas e escapam nomes/conteúdo. Similaridade visual usa hash de imagem e indica candidatos, separadamente da igualdade SHA-256. Compressão não garante redução de tamanho. Conversão PDF para DOCX fica fora conforme solicitado. Ferramentas externas não são instaladas silenciosamente nem há publicação ou commit automático.
