# Agenda do calendário — especificação de design

**Status:** desenho aprovado; aguardando revisão desta especificação antes do plano de implementação.

## Objetivo

Ampliar o widget Calendar existente para cadastrar eventos, aniversários recorrentes e tarefas; mostrar os itens de um dia ao clicar nele; e indicar na Dynamic Island quando existir ao menos uma tarefa não concluída.

## Contexto atual

- `CalendarWidget` mostra uma grade mensal de 42 dias em uma carta fixa de 280 × 152 DIPs. Só há navegação entre meses; os dias não são interativos.
- Ainda não há modelo, armazenamento ou fluxo de tarefas/eventos no app. `PomodoroSchedule` agenda o início de um Pomodoro e não representa tarefas.
- A ilha tem apresentações compactas horizontal e vertical, com contador de Pomodoro e equalizador. O modo compacto usa símbolos estáticos; a forma da ilha já anima com molas.
- A arquitetura mantém regras em `Island.Core`, adaptadores de Windows em `Island.Windows` e composição/interface em `Island.App`.

## Experiência

1. A grade mensal mantém o tamanho e a navegação atuais. Dias com itens recebem marcadores discretos; o dia selecionado tem realce próprio.
2. Clicar em um dia abre uma janela escura de agenda, seguindo a linguagem visual de `ScheduleWindow`. A janela mostra todas as tarefas com aquela data de vencimento, todos os eventos daquele dia e aniversários cuja recorrência coincide com a data.
3. A agenda oferece ações para criar evento, aniversário ou tarefa. O dia selecionado vem preenchido como data inicial nos formulários pertinentes.
4. Eventos têm título, data e horário opcional. Aniversários têm nome e mês/dia e se repetem anualmente. Tarefas têm título, data de vencimento e estado concluído; a conclusão pode ser alternada na lista do dia.
5. Uma tarefa concluída continua visível na agenda do dia correspondente. A marca global da ilha depende somente da existência de tarefas não concluídas.
6. Enquanto existir qualquer tarefa não concluída, inclusive futura, a cápsula compacta mostra um símbolo estático de tarefa. Ele aparece nas apresentações horizontal e vertical e some quando a última tarefa for concluída. O símbolo não substitui o contador de Pomodoro nem o equalizador; o layout reserva espaço para que coexistam sem sobreposição. No modo Mini, a cápsula permanece vazia, conforme o contrato atual desse modo.
7. Mudar de mês e abrir a agenda usam transições breves de opacidade e deslocamento. A implementação respeita `ReduceAnimations` e a preferência de animações do Windows; com movimento reduzido, as mudanças não dependem de deslocamento ou mola adicional.

## Dados e regras

- Eventos e tarefas pertencem a uma única data local. Eventos podem ter horário ou ser de dia inteiro. Recorrência genérica de eventos e de tarefas não faz parte desta entrega.
- Aniversários armazenam apenas nome e mês/dia, sem idade. Uma data de 29 de fevereiro aparece somente em anos bissextos; não é deslocada para outro dia.
- Tarefas vencidas permanecem na data originalmente definida. O símbolo global continua visível até cada tarefa ser concluída, independentemente da data.
- O calendário é local e sem sincronização externa, notificações agendadas ou integração com contas.
- Cada item tem identidade estável para permitir alternar a conclusão sem confundir itens com títulos iguais.

## Organização proposta

- **`Island.Core`**: modelos distintos para evento, aniversário e tarefa; regra para obter itens de um dia e aniversários recorrentes; serviço de agenda e contrato de armazenamento sem dependência de WPF ou Windows.
- **`Island.Windows`**: armazenamento JSON local em `%LocalAppData%\DynamicIsland\calendar.json`, separado de `settings.json`. Escritas são atômicas; JSON corrompido é preservado em quarentena em vez de ser sobrescrito silenciosamente.
- **`Island.App`**: registra o armazenamento e o serviço no ponto de composição, entrega a agenda ao `ShelfContext`, conecta a grade às datas e cria a janela de agenda/formulários. O `IslandViewModel` expõe o estado de existência de tarefas pendentes para as duas cápsulas compactas.
- **Interface**: o widget continua com dimensões fixas para não alterar a geometria das outras cartas. Células de dia e controles da agenda devem ser acessíveis por teclado e expor nomes compreensíveis a leitores de tela.

## Persistência e falhas

- Criar ou concluir um item atualiza a agenda em memória e solicita uma gravação no armazenamento local.
- Se a leitura ou gravação falhar, a interface informa que a alteração não foi salva; não apresenta uma gravação como concluída nem apaga os dados anteriores.
- A ausência de `calendar.json` significa uma agenda vazia. Arquivos de configuração existentes não mudam de formato.

## Critérios de aceite

- Um dia com evento, aniversário ou tarefa tem marcador e abre uma lista contendo todos os itens aplicáveis à data.
- A partir da agenda é possível criar evento com ou sem horário, aniversário anual e tarefa com data.
- A conclusão de uma tarefa é persistida; tarefas concluídas continuam na agenda da data e deixam de contar para o indicador.
- O símbolo da Dynamic Island aparece para qualquer tarefa não concluída, inclusive com vencimento futuro, nas orientações horizontal e vertical; desaparece quando não resta nenhuma.
- Eventos, tarefas e aniversários sobrevivem ao reinício do app, sem misturar dados com `settings.json`.
- O layout não sobrepõe o ícone de tarefa, o contador de Pomodoro ou o equalizador; animações respeitam redução de movimento.

## Fora de escopo

Edição e remoção de itens, conclusão recorrente, lembretes/notificações por horário, sincronização, compartilhamento, detalhes de local/participantes e cálculo de idade.

## Validação prevista

Os critérios de aceite acima definem os fluxos a revisar na implementação, incluindo carga inicial vazia, datas com itens mistos, aniversário em ano comum e bissexto, tarefa futura pendente, alternância de conclusão, coexistência dos símbolos compactos e redução de movimento. Nenhum teste ou build foi executado nesta etapa de design.
