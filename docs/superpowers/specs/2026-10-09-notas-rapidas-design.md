# Notas rápidas na Dynamic Island — especificação de design

**Status:** desenho aprovado pelo usuário em 2026-10-09  
**Projeto:** Dynamic Island para Windows (WPF / .NET 10)

## Objetivo

Adicionar à Estante da Dynamic Island um fluxo rápido para capturar e organizar notas locais. A captura deve exigir poucos passos; a janela de notas deve oferecer espaço e ferramentas suficientes para editar, buscar e recuperar conteúdo. A implementação deve seguir os limites de camadas existentes e o estilo de movimento da ilha.

## Escopo da primeira versão

- Widget **Notas** adicionável à Estante, com estado vazio útil, botão de nova nota e até duas notas recentes/fixadas.
- Janela normal e focável, aberta pelo widget ou por atalho global, com lista de notas pesquisável à esquerda e editor à direita.
- Texto simples com título, conteúdo e itens de checklist.
- Salvamento automático, local e persistente.
- Etiquetas, cores, fixação, ordenação por fixadas e edição recente, arquivo e lixeira recuperável.
- Busca local por título, conteúdo e etiquetas.
- Atalhos `Ctrl+Alt+N` para abrir a captura global, `Ctrl+N` para criar nota e `Ctrl+F` para buscar. O atalho global pode ser desligado nas configurações.
- Interface em português, navegação por teclado, foco visível e nomes acessíveis nos controles.

Ficam fora desta versão: sincronização em nuvem, anexos e lembretes. Lembretes exigem que o agendamento sobreviva ao fechamento e reinício do aplicativo; devem ser avaliados como um projeto posterior.

## Fluxo e comportamento

1. O usuário adiciona **Notas** pela personalização da Estante. O identificador do widget é estável (`quicknotes`).
2. O widget mostra as duas notas que encabeçam a ordem atual. Em estado vazio, a ação de criar permanece visível.
3. O botão `+` ou `Ctrl+Alt+N` abre a janela de notas, cria um rascunho e foca o título. Uma nota só passa a existir quando recebe conteúdo; fechar um rascunho vazio não cria lixo.
4. `Ctrl+N` inicia outra nota e `Ctrl+F` foca a busca. Selecionar outra nota ou fechar a janela aguarda a gravação pendente.
5. Alterações são salvas após uma pausa curta na digitação e após troca de nota/fechamento. A interface apresenta estados de salvamento e erro. Uma falha não deve apagar o texto ainda disponível na sessão nem ser apresentada como sucesso.
6. A busca considera título, conteúdo e etiquetas. A lista principal exclui itens arquivados e na lixeira; itens fixados aparecem primeiro, seguidos por `UpdatedAt` decrescente.
7. Arquivar retira a nota da lista principal e permite encontrá-la na área **Arquivo**. Excluir move a nota para **Lixeira**. É possível restaurá-la ou excluí-la permanentemente; não há descarte automático silencioso.

Se `Ctrl+Alt+N` já estiver registrado por outro processo, o restante do app continua disponível. A janela e o widget indicam que o atalho não foi registrado; a falha é registrada no log. A opção pode ser desativada nas configurações.

## Interface, movimento e acessibilidade

- O widget mede uma linha padrão da Estante e reutiliza os componentes e cores existentes. Exibe título, prévia curta e ação explícita, sem empilhar muitos controles no cartão.
- A janela usa superfície escura e contraste tipográfico já usados pelo app. Busca e lista ficam separadas do editor; etiquetas, cor, checklist, fixação, arquivo e exclusão são ações identificáveis e acessíveis.
- Inserir, remover, selecionar ou reordenar notas pode usar transições curtas de opacidade e deslocamento. Não haverá animação em repouso, brilho pulsante ou partículas.
- A preferência existente **Reduce animations** reduz ou remove movimento nas novas telas, mantendo o feedback necessário para indicar mudanças.
- Controles têm nomes acessíveis, ordem de tabulação previsível, foco visível e operação por teclado. Cor nunca é o único indicador de estado.

## Arquitetura

- `Island.Core/Notes` contém o modelo, operações e regras independentes de plataforma. O serviço de notas expõe snapshots e comandos de criar, editar, fixar, arquivar, excluir, restaurar e remover permanentemente.
- Um contrato de armazenamento no Core separa as regras da persistência. `Island.Windows` implementa esse contrato com armazenamento JSON; nenhuma dependência de banco de dados é necessária.
- `Island.App` compõe o serviço por injeção de dependências e apresenta o widget e a janela em componentes separados. O widget participa do catálogo e da configuração da Estante; a janela de notas permanece uma janela WPF focável, independente da janela não ativável da ilha.
- O atalho global reutiliza `Island.Windows.Input.GlobalHotkey` e encaminha a abertura/foco para a thread de UI. A ativação por widget continua funcional se o registro global falhar.
- A implementação respeita o princípio de movimento do projeto: uma transição finita por ação, sem trabalho de renderização contínuo em repouso.

## Modelo e persistência

Cada nota contém ID estável, título, conteúdo, itens de checklist, etiquetas, chave de cor, datas de criação/edição, estado fixado/arquivado e data de exclusão opcional. Checklist mantém texto e estado concluído por item. Etiquetas não vazias são normalizadas para comparação sem distinção de maiúsculas/minúsculas.

Os dados ficam em `%LocalAppData%\\DynamicIsland\\notes.json`, separados de `settings.json`, com número de versão do formato. O armazenamento grava primeiro em arquivo temporário e substitui o destino atomicamente. Carregamento de arquivo ausente retorna lista vazia. JSON inválido é preservado em arquivo de recuperação e gera aviso/log; o primeiro salvamento não pode destruir silenciosamente a cópia recuperável. Alterações não dependem da conta nem da rede.

## Critérios de aceitação

- Uma nota criada pela Estante e outra criada por `Ctrl+Alt+N` sobrevivem ao encerramento e reinício do aplicativo.
- Falha de conflito do atalho global não impede criação pela Estante nem edição na janela.
- Busca localiza texto pelo título, conteúdo e etiqueta; fixadas e editadas recentemente aparecem na ordem definida.
- Checklist, etiquetas, cor, fixação e estado de arquivo persistem depois de reiniciar.
- Uma nota movida para a lixeira pode ser restaurada; exclusão permanente requer ação explícita.
- Salvar falha de forma visível e conserva o texto em edição na sessão.
- A janela e o widget permanecem utilizáveis por teclado, e **Reduce animations** respeita a preferência atual.
