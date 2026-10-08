# Fase 7 — Checklist de estabilidade

## Automatizado (rodar com `dotnet test`)
- `StressTests` (Core): tempestades de eventos, 3000 ciclos abrir/fechar, posts concorrentes, Dispose, callbacks obsoletos. Garantem 0 timers pendentes ao final.
- `ResourceLeakTests` (Windows): 200 ciclos criar/iniciar/descartar de cada serviço (volume, mídia, clipboard, tela cheia, hotkey, guard de sites) e crescimento de handles < 50.
- `ExplorerRestartTests`: mensagem `TaskbarCreated` registrada.

## Soak (longa duração)
```
dotnet run --project src/Island.App -c Release -- --soak=30
```
Resultado em `%LocalAppData%\DynamicIsland\logs\island-*.log` (linhas `SOAK`). PASS = delta de handles < 100 e de working set < 50 MB após 60 s de aquecimento.
Medido (Debug, 2 min, 3836 eventos): ws ~200 MB estável (delta -3 MB), handles 1846 (delta -4), threads 28. Repetir em Release por 30+ min antes de liberar.

## Manual (não automatizável)
Marcar ao testar. Em cada item: a ilha não rouba foco, cliques atravessam a área transparente, sem CPU contínua.
- [ ] DPI 100%, 125%, 150%, 200%: posição centralizada, sem borda, texto nítido.
- [ ] Dois monitores: escolher monitor nas configurações; mover entre monitores com DPI diferente.
- [ ] Desconectar/conectar monitor com o app aberto.
- [ ] Jogo em tela cheia exclusiva e em tela cheia sem borda: ilha some (HideInFullscreen) e volta ao sair.
- [ ] Alt+Tab: a ilha não aparece na lista e não perde o topo.
- [ ] Win+L (bloquear) e desbloquear: ilha reaparece (Nudge).
- [ ] Suspender e retomar o PC: ilha reaparece, mídia e volume atualizados.
- [ ] Reiniciar o Explorer (`taskkill /f /im explorer.exe` e `start explorer.exe`): ilha continua no topo e o ícone da bandeja volta, sem piscar duplicado.
- [ ] Trocar dispositivo de áudio padrão: volume segue o novo dispositivo.
- [ ] Spotify/navegador: abrir, pausar, trocar e fechar sem cartão antigo preso.
- [ ] Sessão de 8 h aberta: CPU parada < 1%, RAM estável (Gerenciador de Tarefas).

## Limitações conhecidas
- `IslandCoordinator.Start()` lê os valores iniciais fora do lock; um evento que chegue nessa janela (microssegundos na partida) pode ser sobrescrito pelo valor semeado até o próximo evento.
- Se um handler de `StateChanged` lançar, o restante da fila pendente só é drenado no próximo evento.
- Mudar `HideInFullscreen` com um app em tela cheia ativo só é reavaliado no próximo evento.
