# Órbita de arquivos — desenho e execução

Solicitação: desenhar e implementar diretamente, sem confirmação intermediária.

A ilha aceita arquivos e pastas em qualquer modo visível. Ao arrastar sobre ela, um menu circular surge no ponto de entrada, limitado à área útil do monitor. O desenho atual segue **Ilha de arquivos_ painel de ações.html** fornecido: setores arredondados, ícone e rótulo dentro de cada fatia, centro informativo e cores por ação. O desktop permanece visível fora da roda e entre as fatias, sem fundo de página ou grade. É possível soltar diretamente em uma ação ou soltar na ilha e escolher depois. Soltar fora ou cancelar o arrasto dispensa a prévia. Reduzir animações elimina movimentos e partículas. Escape e Fechar dispensam o menu.

Escolher uma ação abre opções explícitas, lista dos arquivos e Executar. Selecionar nunca executa. Trabalho em segundo plano, cancelamento e status mantêm a interface responsiva. Resultados vão para uma pasta nova escolhida pelo usuário; originais não são alterados. Renomeação em lote produz cópias com prévia de nomes. ZIP preserva pastas; extração rejeita caminhos externos e links. Duplicados são relatados, nunca apagados.

## Painel atual: Ilha de arquivos

Cápsula de indicação na posição da ilha, com estados de 210 × 46 e 256 × 52 DIP e acento da ação sob o ponteiro. A ilha original fica invisível durante o painel e volta ao fechar. Radial transparente de 420 × 520 DIP (incluindo cápsula e bandeja); entrada em escala 0,12, rotação −40° e deslocamento vertical −300, durante 800 ms com 55 ms entre setores. A fatia ativa usa texto escuro, conforme o novo HTML.

Escolher abre uma superfície arredondada de até 576 × 680 DIP, limitada ao monitor, com expansão elástica a partir da cápsula. Todas as ações abre uma grade de três colunas, com chips coloridos e cartões que sobem no hover. É possível voltar ao radial. Formatos são seletores segmentados; largura e qualidade têm sliders e valores editáveis. A prévia usa nomes reais, tamanho dos arquivos e miniatura local quando suportada. O botão Executar continua sendo obrigatório.

O progresso usa atividade indeterminada e mensagens reais do serviço, sem porcentagem ou redução de tamanho inventadas. Conclusão, cancelamento e falha têm estados próprios; abrir resultados e fazer outra ação ficam disponíveis após o processamento. Os exemplos de link público e opções sem suporte no núcleo não são oferecidos como operações funcionais. Fonte Sora incorporada, transparência no radial e redução de animações preservadas. O HTML foi usado como referência estática de desenho, sem executar seu script.

## Implementação

1. Núcleo `Island.Core/Files`: catálogo por tipos de arquivo, opções, operações, relatórios, ZIP, dados e PDF. Testes reais com arquivos temporários, incluindo colisões, escape de ZIP, CSV citado e PDF reaberto.
2. Conversores: Magick.NET-Q8-x64 incorporado para imagens, miniaturas e hashes visuais; FFmpeg detectado no PATH ou pasta tools para áudio e vídeo; LibreOffice para documentos; Ghostscript para compressão PDF. Dependências ausentes são informadas antes da execução. PDFsharp 6.2.2 para PDFs, com dependência criptográfica corrigida.
3. `FileOrbitWindow`: seletor radial, opções por operação, destino, progresso e cancelamento. Arrasto da ilha integrado por partial separado, preservando widgets e trabalho local.
4. Verificação: testes do núcleo, testes WPF renderizados, conversões reais FFmpeg e suíte da solução. Registrar limites de homologação.

## Referência anterior: Radial Drop

Geometria portada do SVG: canvas lógico 520 × 520, centro (260,260), raios 105/245, recuo angular equivalente a 9 unidades, stroke 10 e junções arredondadas. O centro tem raio 84 e o anel pontilhado gira em 50 segundos. A janela usa 420 × 460 DIP, incluindo controles discretos da bandeja. O número de setores acompanha o lote real (quatro a sete); ícones e ações demonstrativas de e-mail/link/exclusão não substituem as funções existentes.

Entrada como no CSS: escala 0,5, rotação −26°, 700 ms com overshoot e diferença de 55 ms por setor. Hover: setor inteiro recebe sua cor, brilho de 16 unidades, translação radial de 10, escala 1,045 e ícone 1,18; o centro informa a ação. Confirmação inclui flash e partículas antes de abrir os ajustes; ela não executa a operação. O WPF faz hit-test pela geometria estática da fatia, evitando que os controles de canvas inteiro bloqueiem os vizinhos ou o centro. Ao voltar, cores e deslocamentos são restaurados.

A fonte [Sora](https://github.com/google/fonts/tree/main/ofl/sora) e sua licença OFL são incorporadas ao aplicativo, sem download em tempo de uso. O HTML foi lido como referência de desenho, sem executar seu JavaScript. A abertura de arquivo local no navegador foi bloqueada pela política do navegador; validação visual feita por renderização WPF nativa. Os rótulos nas direções quase horizontais foram ajustados para não cruzar os ícones quando há quatro, cinco ou sete setores.

## Desenho compacto anterior

A revisão aprovada troca as pílulas por ícones vetoriais de 56 DIP, rótulos curtos e áreas fixas de 80 × 74 DIP. O raio passa de 220 para 110 DIP e a cena de 720 × 640 para 340 × 360 DIP. O centro permanece vazio e todo o espaço entre ações permanece transparente. O nome completo continua acessível na dica e na automação de cada botão. A folha de contato usa o rótulo curto “Mosaico”.

Entrada: deslocamento e escala apenas do conteúdo visual, 260 ms, escalonamento de 12 ms, BackEase discreto e fade de 130 ms. Hover: 110 ms e escala 1,07. Saída: 150 ms. A área de cada botão não se move; fica disponível após a entrada para evitar seleção invisível. Reduzir animações remove os movimentos. Os rótulos têm uma pequena superfície própria para manter contraste sobre qualquer desktop; não existe fundo compartilhado.

Selecionar uma ação expande a janela para os ajustes em 680 × 620 DIP, respeitando a área útil do monitor. Voltar restaura exatamente os limites do menu compacto. A prévia de arrasto e a execução explícita continuam independentes. Testes verificam colisões de alvos com 4, 5 e 7 ações, transparência, geometria, limites de monitor, animações e restauração.

Referências de desenho: [temas Kando](https://kando.menu/menu-themes/), [anatomia dos temas e motion](https://kando.menu/create-menu-themes/), [Fluent motion](https://fluent2.microsoft.design/motion) e [transformações WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/transforms-overview). Valores acima são a adaptação aprovada para a ilha, não especificações impostas por essas referências.

## Limites e revisão

Relatórios HTML locais incluem miniaturas incorporadas e escapam nomes/conteúdo. Similaridade visual usa hash de imagem e indica candidatos, separadamente da igualdade SHA-256. Compressão não garante redução de tamanho. Conversão PDF para DOCX fica fora conforme solicitado. Ferramentas externas não são instaladas silenciosamente nem há publicação ou commit automático.
