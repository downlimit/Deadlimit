# Deadlimit

**Ferramentas para artistas que criam mods para Deadlock.**

O Deadlimit reúne o pipeline fragmentado de modding de personagens de Deadlock em um único fluxo de trabalho voltado para artistas: obter os assets originais do jogo, criar modelos e texturas, preparar o projeto para o Reduced CSDK, iterar nas ferramentas de autoria, gerar um VPK e testar o mod no Deadlock retail.

Ele elimina grande parte do trabalho repetitivo de Source 2 que normalmente exigiria procurar arquivos dentro de VPKs, editar caminhos de recursos manualmente, mover arquivos entre diferentes ferramentas e reconstruir a mesma estrutura a cada iteração.

**Gratuito · Código aberto · Windows**

[English](README.md) · [Русский](README.ru.md) · [简体中文](README.zh-CN.md) · [Português (Brasil)](README.pt-BR.md)

---

## Download

### Para artistas

**[Baixar `Install-Deadlimit.cmd`](https://github.com/downlimit/Deadlimit/raw/refs/heads/main/Install-Deadlimit.cmd)**

Coloque o instalador na pasta em que você quer manter o Deadlimit e execute-o.

O Deadlimit cria uma pasta `Deadlimit` ao lado do instalador e instala o programa nela.

```text
D:\Tools\Install-Deadlimit.cmd
D:\Tools\Deadlimit\
```

Você não precisa conhecer Git nem configurar o .NET SDK manualmente. Se o Git for Windows ou o .NET 10 SDK estiver ausente, o instalador mostra o que é necessário e pede sua autorização antes de instalar o componente pelo WinGet.

As versões iniciais do Deadlimit ainda não são assinadas digitalmente, portanto o Windows SmartScreen pode exibir um aviso de editor desconhecido. Execute somente o instalador baixado do repositório oficial `downlimit/Deadlimit`.

### Para desenvolvedores e colaboradores

Se você quiser modificar o próprio Deadlimit, enviar correções ou contribuir com novos recursos, clone o repositório:

```powershell
git clone https://github.com/downlimit/Deadlimit.git
cd Deadlimit
.\DeadlimitManager.cmd
```

Um checkout de desenvolvimento requer o .NET 10 SDK. Consulte [CONTRIBUTING.md](CONTRIBUTING.md) para o fluxo de contribuição.

---

# Deadlimit Manager

O **Deadlimit Manager** é o aplicativo desktop principal e o centro do fluxo de trabalho.

### Projetos

Cada mod existe como um projeto separado no Deadlimit Manager. O Manager mantém esses projetos em uma biblioteca e acompanha o personagem selecionado, os arquivos-fonte, o conteúdo de autoria gerado, o slot de release e o estado do pipeline.

### Obter assets originais

Selecione um personagem de Deadlock e o Manager recuperará, a partir dos dados atuais da versão retail do jogo, os recursos originais compatíveis necessários como base de trabalho.

Isso substitui a procura manual em VPKs, caminhos de modelos, materiais e dependências relacionadas.

### Preparar para CSDK

O Manager transforma os arquivos-fonte do artista em um workspace de autoria para o Reduced CSDK.

Ele cuida da organização dos arquivos, de correções conhecidas de exportadores e caminhos, da preparação do modelo, da estrutura inicial de materiais, da associação das texturas e de outras transformações específicas de Source 2 que normalmente exigiriam edição manual de arquivos.

O projeto preparado continua sendo uma etapa de autoria editável. Você pode abri-lo no CSDK/ModelDoc, trabalhar nos materiais e shaders, salvar as alterações e continuar iterando sem transformar o processo em um conversor opaco de um clique.

### Live Sync

Mantenha o CSDK aberto enquanto trabalha.

O Deadlimit Manager monitora alterações compatíveis no projeto e as sincroniza automaticamente com o projeto já preparado no CSDK. Alterações em DMX, texturas e Vertex Color podem ser atualizadas sem repetir todo o ciclo manual de preparação e cópia. Mudanças estruturais ou em referências de materiais acionam a preparação completa necessária enquanto o CSDK permanece aberto.

### Build & Test

Quando o projeto estiver pronto para um teste dentro do jogo, **Build & Test** executa o pipeline do lado da release.

O Deadlimit Manager prepara o estado mais recente do projeto, compila os recursos Source 2 alterados, restaura após a compilação os vínculos de animação necessários do personagem, verifica a saída, empacota o addon em um VPK e o implanta no slot local configurado de addons do Deadlock.

A etapa de autoria no CSDK permanece limpa: a correção dos vínculos de animação acontece depois da compilação, permitindo que o artista continue usando o CSDK para trabalhar no ModelDoc e nos materiais antes da build final de teste.

### Importar e reparar VPKs existentes

O Deadlimit Manager também pode importar um `pak##_dir.vpk` existente como projeto.

O payload compilado importado é preservado em vez de passar novamente pelo compilador normal de autoria. Durante **Build & Test**, o Deadlimit Manager pode comparar os vínculos de animação do personagem com o modelo atual do Deadlock retail, corrigir vínculos ausentes ou desatualizados, reconstruir o VPK, verificar o resultado e implantá-lo novamente no slot de release adotado.

Esse caminho de reparo é propositalmente restrito: ele trata a classe de problemas relacionada a vínculos de animação, sem fingir ser um botão universal capaz de reparar qualquer problema possível em um mod.

### Gerenciamento da toolchain

O Manager centraliza as ferramentas externas usadas no modding de Deadlock.

Ele pode localizar e validar o Deadlock, gerenciar o Reduced CSDK e o DeadlockTools, verificar o estado das ferramentas compatíveis e executar utilitários auxiliares, como o DepotDownloader, quando um fluxo específico precisar deles.

---

# Deadlimit Scripts

**Deadlimit Scripts** são as ferramentas do lado do DCC usadas durante a etapa de autoria de modelos.

A implementação atualmente incluída é baseada em MAXScript. O suporte ao Blender está planejado dentro do mesmo produto Deadlimit Scripts, em vez de ser tratado como uma ferramenta separada.

### Bone Tools

Esqueletos Valve em DMX nem sempre ficam convenientes para trabalho imediatamente após a importação.

Bone Tools pode ajustar o comprimento e a espessura visual dos bones com base na hierarquia, inverter a geometria de exibição sem alterar o rig e restaurar bones compatíveis que tenham sido convertidos acidentalmente para Editable Poly, preservando identidade do node, hierarquia, animação e referências do Skin.

### Vertex Color

O Deadlimit Scripts facilita a criação e a verificação de Vertex Color antes de o modelo chegar ao jogo.

Você pode transferir cores entre a paleta do objeto e Vertex Color, alternar a exibição no viewport, distribuir dados de Vertex Color/material/paleta entre meshes e preservar a stack de modifiers existente nas operações compatíveis.

Para visualização no engine, o Deadlimit Manager pode preparar um material que exibe Vertex Color diretamente no CSDK.

Se um exportador DMX perder Vertex Color, **Export Vertex Color FBX** grava um arquivo auxiliar `*_vertexcolor.fbx`. Durante o Prepare, o Manager pode detectar esse sidecar e transferir automaticamente os dados de cor de volta para a mesh DMX correspondente.

### Inner Lineart

**Inner Lineart** transforma o comportamento de outline por backfaces expandidas do Deadlock em uma ferramenta de autoria para linhas gráficas internas no design do personagem.

Selecione as edges internas desejadas, defina a largura da linha e o Deadlimit Scripts gera uma geometria de lineart separada com a orientação de faces e a direção de normais necessárias. Quando aplicável, o resultado pode preservar UVs de origem, Vertex Color, IDs de material, transforms e Skin.

Isso permite criar traços gráficos internos como parte do próprio modelo, em vez de limitar o visual de lineart do Deadlock ao contorno externo da silhueta.

---

# Deadlimit Shade

**Em desenvolvimento.**

**Deadlimit Shade** é a parte do toolkit dedicada à autoria de texturas no Substance 3D Painter.

O objetivo é transformar o Painter em um ambiente de preview útil para materiais de personagens de Deadlock, evitando que artistas de textura precisem avaliar o trabalho em um viewport PBR genérico e só descubram o resultado real depois de levar o material para o Source 2.

O protótipo atual já inclui shaders para Painter orientados ao Deadlock, perfis de personagens, ferramentas de preview de outline, utilitários para inspeção de materiais/texturas da versão retail e um dock do Painter que aplica a configuração de preview do Deadlimit Shade a projetos compatíveis.

Fluxo de trabalho pretendido:

```text
Substance 3D Painter
        ↓
Deadlimit Shade
        ↓
Deadlimit Manager
        ↓
Deadlock
```

O objetivo é oferecer paridade prática durante a autoria, sem afirmar que o Painter reproduz o Source 2 pixel a pixel.

---

## Fluxo de trabalho

```text
Assets do Deadlock retail
        ↓
Deadlimit Manager
        ↓
DCC + Deadlimit Scripts
        ↓
Substance 3D Painter + Deadlimit Shade
        ↓
Deadlimit Manager
Prepare / Live Sync / Build & Test
        ↓
Reduced CSDK
        ↓
VPK
        ↓
Deadlock retail
```

O Deadlimit Shade ainda está em desenvolvimento e é opcional no pipeline atual de autoria de modelos.

---

## Status do projeto

O Deadlimit é desenvolvido ativamente em um ecossistema de Deadlock / Source 2 que continua mudando.

Windows é a plataforma atualmente compatível. A implementação incluída do Deadlimit Scripts é baseada em MAXScript, o suporte ao Blender está planejado e o Deadlimit Shade está em desenvolvimento ativo.

Atualizações do jogo e das ferramentas externas podem exigir mudanças no pipeline. As versões testadas e o estado atual de compatibilidade estão em [COMPATIBILITY.md](COMPATIBILITY.md).

---

## Ajuda

- [Compatibilidade](COMPATIBILITY.md)
- [Changelog](CHANGELOG.md)
- [Suporte](SUPPORT.md)
- [Reportar um bug ou solicitar um recurso](https://github.com/downlimit/Deadlimit/issues)

---

## Desenvolvimento

O Deadlimit é open source. Os requisitos de desenvolvimento, contribuição, DCO e pull requests estão documentados em [CONTRIBUTING.md](CONTRIBUTING.md).

---

## Licença e independência

O código-fonte do Deadlimit é distribuído sob a [Licença MIT](LICENSE). Consulte [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) para avisos sobre dependências e ferramentas externas e [SECURITY.md](SECURITY.md) para o envio privado de relatórios de vulnerabilidade.

O Deadlimit interoperabiliza com ferramentas de terceiros instaladas pelo usuário e com conteúdo local do jogo; ele não distribui esses componentes. O Deadlimit é um projeto independente da comunidade e não é afiliado, patrocinado, endossado nem aprovado pela Valve, Autodesk, Adobe, Wall Worm ou pelos mantenedores das demais ferramentas que pode executar.
