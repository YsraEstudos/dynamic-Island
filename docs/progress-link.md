# Progress link (SuperPrograma → Dynamic Island)

A tarefa longa da Suite (corte de vídeo, extração de quadros, junção de imagens, conversão, ...) mostra o
percentual na ilha. A Suite executa o trabalho; a ilha só mostra. Um sentido só: Suite → ilha.

## Por que named pipe
- Orientado a eventos: a ilha fica bloqueada em `WaitForConnectionAsync`, sem timer e sem polling de arquivo.
- Sem disco: nada é gravado por atualização.
- Ilha fechada = a Suite não paga nada (uma tentativa de conexão a cada 5 s, e só quando há progresso a enviar).

## Transporte
- Pipe: `\\.\pipe\DynamicIsland.Progress.v1` (`PipeNames.Progress`). Servidor na ilha, cliente na Suite.
- ACL: só o usuário atual (o servidor cria com `PipeSecurity` para o SID do usuário). Sem acesso remoto.
- Mensagens: UTF-8, JSON, **uma por linha** (`\n`), no máximo 4 KiB por linha. A ilha ignora linha maior, JSON
  inválido, `v` diferente de 1 e campos desconhecidos. Nunca lança por causa do conteúdo.

## Mensagem (`v` = 1)
```json
{"v":1,"id":"1234-7","title":"Cortar: aula.mp4","category":"Corte","state":"running","percent":42.5,"message":"Cortando...","source":"SuperPrograma"}
```
| campo | tipo | regra |
|---|---|---|
| `v` | int | obrigatório, `1` |
| `id` | string | obrigatório, único por tarefa (`{pid}-{taskId}`), até 64 chars |
| `title` | string | obrigatório, a ilha corta em 80 chars |
| `category` | string? | opcional ("Corte", "Conversao", ...) |
| `state` | string | `queued` `running` `done` `error` `cancelled` |
| `percent` | number? | 0..100; `null`/ausente = indeterminado; a ilha faz clamp |
| `message` | string? | etapa atual ou texto do erro; a ilha corta em 120 chars |
| `source` | string? | origem ("SuperPrograma") |

## Regras do remetente (Suite)
- No máximo ~4 mensagens/s por tarefa (coalescer, a última vence). Mudança de `state` e 0 %/100 % saem na hora.
- Não bloqueia a interface nem a tarefa: fila + thread daemon; falha de pipe é engolida.
- Sem ilha aberta: tentar de novo no máximo a cada 5 s, e só quando houver algo novo para enviar.
- Ao terminar sempre enviar um estado final (`done`/`error`/`cancelled`).

## Regras do receptor (ilha)
- Máximo de 16 tarefas ativas; a mais antiga é descartada.
- Conexão encerrada = tarefas daquela conexão ainda ativas somem, sem aviso.
- `done` e `error` geram um aviso (`Notice`) e ficam 4 s na lista antes de sair. `cancelled` some sem aviso.
- A ilha calcula o tempo restante (ETA) a partir do percentual e do tempo decorrido.
- A barra na cápsula só expande a ilha quando a tarefa termina ou falha.

## Mapa de código
| Camada | Arquivos |
|---|---|
| Suite | `shared/island_link.py`, `shared/ffmpeg_progress.py`, `shared/task_runner.py` (`report_progress`), serviços de `video_cutter`, `video_transcriber`, `image_joiner` |
| Core | `Island.Core/Progress/*` (modelos, `ProgressMessageParser`, `ProgressTracker`) |
| Windows | `Island.Windows/Progress/NamedPipeProgressFeed` |
| App | `ViewModels/ProgressPresenter`, `Views/CompactView` + `VerticalCompactView` (barra), `Views/Widgets/TasksWidget` |
