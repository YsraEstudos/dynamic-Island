# Arquitetura

Fluxo: evento do Windows → adaptador (Island.Windows) → `IslandCoordinator` (Island.Core) decide por prioridade →
`IslandStateReducer` calcula o próximo `IslandState` → `IslandViewModel` → `IslandWindow` anima o mesmo shape (molas).

Regras:
- `Island.Core` não referencia nenhum outro projeto. `Island.Windows` implementa os contratos; `Island.App` compõe tudo (App.xaml.cs).
- Um único coordenador e um único timer temporário. Eventos de volume repetidos reutilizam o mesmo estado.
- Sem polling de mídia/volume: tudo orientado a eventos. A ilha em repouso não anima (CompositionTarget.Rendering só fica ligado enquanto houver mola em movimento).
- A janela é WPF transparente com WS_EX_NOACTIVATE/TOOLWINDOW; áreas transparentes deixam o clique passar.

## Linguagem de movimento
Uma forma só, sem cortes: largura, altura e raio têm molas separadas (overshoot leve, ζ≈0.8). O conteúdo troca com blur rápido:
saída (~100 ms) termina antes da entrada (~160 ms). Sem easing saltitante, brilhos, partículas ou gradientes no chrome.
