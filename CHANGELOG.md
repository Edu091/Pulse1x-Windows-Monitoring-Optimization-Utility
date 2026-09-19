# Histórico de versões

## 1.4.8 — 18/09/2026

- A seção Sobre passa a exibir a versão realmente em execução: ela estava fixada em "1.0.0" desde o primeiro lançamento.
- Adicionado o histórico de versões à seção Sobre, disponível sem conexão com a internet.

## 1.4.7 — 18/09/2026

- Corrigidos os campos ilegíveis nas janelas do GameHub: as caixas de seleção e de texto do editor de perfis apareciam como blocos brancos com o texto invisível.
- As janelas de emulador e de edição de jogo passam a seguir a mesma aparência do restante do GameHub.
- Corrigida também a lista suspensa das caixas de seleção, que é exibida fora da janela e mantinha as cores claras.
- O instalador agora identifica corretamente o Pulse1x em execução mesmo quando executado como administrador, fechando e reabrindo o aplicativo para concluir a atualização.

## 1.4.6 — 18/09/2026

- O botão B do controle passa a levar o seletor para os botões de salvar ou sair antes de fechar uma tela de configuração, em vez de descartar os ajustes de imediato.

## 1.4.5 — 18/09/2026

- Corrigida a inversão do analógico causada pelo retorno da alavanca ao centro.
- Corrigido o anel de foco desalinhado no menu lateral.
- Corrigido o tema da janela de perfis, que exibia texto escuro sobre fundo claro.

## 1.4 — 18/09/2026

Introdução do **GameHub**, uma biblioteca unificada de jogos com perfis de sistema por jogo.

**Biblioteca e capas**

- Detecção automática de jogos instalados pela Steam, Epic Games, GOG, EA e Ubisoft Connect, lendo os catálogos locais dos próprios lançadores — sem login e sem internet no caso da Steam.
- Busca de capas pelo nome através do catálogo público da Steam, o que também ilustra itens da Epic, GOG, adicionados manualmente e ROMs. Sem resultado, usa o ícone do executável e, por fim, uma capa gerada com o nome do jogo.
- Remoção de duplicados entre lojas: um título registrado por duas delas aparece uma única vez.
- Adição manual de executáveis, atalhos, pastas e emuladores com suas ROMs.

**Perfis por jogo**

- Cada seção funciona como "não alterar / automático / personalizado": plano de energia e ajustes avançados, modo do fabricante em notebooks, prioridade e afinidade de processo, otimização de memória, latência, rede, áudio, vídeo e os aplicativos a fechar ou abrir junto com o jogo.
- Todos os valores são lidos antes de serem alterados e gravados em disco, de modo que uma queda do jogo, do Pulse1x ou do computador ainda restaure o estado anterior na próxima execução.
- Predefinições editáveis: Padrão, Competitivo, Máximo Desempenho, Balanceado, Silencioso e Economia.

**Interface e controle**

- Navegação completa por controle (XInput), com menu lateral, teclado virtual para a busca e animações de abertura.
- Métricas de uso: tempo de jogo, FPS médio e máximo, médias por dia e uma seção de estatísticas.
- Personalização visual do aplicativo: cores, transparência, desfoque, intensidade das animações e plano de fundo.

Também nesta série: correção da leitura de acentos na saída do `powercfg`, que criava planos de energia duplicados a cada execução.

## 1.1.1 — 12/07/2026

- Corrigida a abertura do aplicativo logo após a instalação, que falhava com erro de elevação 740.

## 1.1.0 — 12/07/2026

- Novo painel **Status dos Servidores** dentro de Latência, com cartões por empresa nas categorias Jogos, Comunicação, Streaming, IA, Infraestrutura e Armazenamento, busca e atualização automática.
- Situação obtida das APIs oficiais quando disponíveis, com verificação de acessibilidade como alternativa — antes a maioria das empresas aparecia como fora do ar.
- Seções mais densas da página de Latência agora podem ser recolhidas.
- Corrigido o aviso de "atualização disponível" que aparecia a cada abertura.
- Corrigida a demora na atualização do ícone na busca do Menu Iniciar após atualizar.

## 1.0.1 — 11/07/2026

- Atualização automática pelas versões publicadas no GitHub, com aviso na abertura quando há uma versão nova.
- Falhas na instalação da atualização passam a ser exibidas, com nova tentativa quando um antivírus bloqueia o arquivo temporariamente.
- Melhorias na tradução de várias partes do aplicativo.

## 1.0.0 — 28/06/2026

- Primeira versão do Pulse1x: monitor de hardware para Windows.
