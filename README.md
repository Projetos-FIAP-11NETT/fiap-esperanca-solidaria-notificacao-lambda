# FIAP CloudGames — Notifications Lambda

Serviço serverless responsável pelo envio de e-mails transacionais da plataforma CloudGames. A função é acionada por mensagens em uma fila SQS e envia os e-mails via AWS SES, com rastreamento distribuído e logs estruturados integrados ao New Relic.

---

## Sumário

- [Visão Geral](#visão-geral)
- [Arquitetura](#arquitetura)
- [Tecnologias](#tecnologias)
- [Pré-requisitos](#pré-requisitos)
- [Estrutura do Projeto](#estrutura-do-projeto)
- [Variáveis de Ambiente](#variáveis-de-ambiente)
- [Execução Local com LocalStack](#execução-local-com-localstack)
- [Deploy na AWS (Produção)](#deploy-na-aws-produção)
- [Testes](#testes)
- [Observabilidade](#observabilidade)
- [Troubleshooting](#troubleshooting)

---

## Visão Geral

Quando um evento de notificação é publicado na fila SQS, esta Lambda é invocada automaticamente. Ela desserializa a mensagem, envia o e-mail pelo SES e registra o resultado no New Relic (logs + traces OpenTelemetry).

**Fluxo resumido:**

```
Produtor → SQS Queue → Lambda (EmailFunction) → AWS SES → E-mail entregue
                                     ↓
                              New Relic (logs + traces)
```

---

## Arquitetura

```
┌─────────────────────────────────────────────────────────┐
│                       AWS / LocalStack                   │
│                                                         │
│  ┌──────────────┐     ┌───────────────────────────────┐ │
│  │  SQS Queue   │────▶│     Lambda: EmailFunction     │ │
│  │notification- │     │                               │ │
│  │   queue      │     │  1. Deserializa EmailMessage  │ │
│  └──────────────┘     │  2. Cria span OpenTelemetry   │ │
│                        │  3. Envia log ao New Relic    │ │
│                        │  4. Chama SesEmailService     │ │
│                        └──────────────┬────────────────┘ │
│                                       │                  │
│                               ┌───────▼────────┐         │
│                               │   AWS SES      │         │
│                               │ (envio de      │         │
│                               │  e-mail)       │         │
│                               └────────────────┘         │
└─────────────────────────────────────────────────────────┘
                         │
                ┌────────▼────────┐
                │   New Relic     │
                │ Logs + Traces   │
                │ (OTLP/HTTP)     │
                └─────────────────┘
```

**Recursos provisionados pelo Terraform:**

| Recurso | Nome padrão | Descrição |
|---------|-------------|-----------|
| IAM Role | `lambda-role` | Role de execução da Lambda |
| SQS Queue | `notification-queue` | Fila de entrada de notificações |
| Lambda Function | `email-function` | Processador de e-mails |
| SES Email Identity | `no-reply@fiapcloudgames.local` | Remetente verificado |
| Event Source Mapping | — | Gatilho SQS → Lambda (batch size: 1) |

---

## Tecnologias

| Tecnologia | Versão | Uso |
|---|---|---|
| .NET | 8.0 | Runtime da Lambda |
| AWS Lambda | — | Plataforma serverless |
| Amazon SQS | — | Fila de mensagens (trigger) |
| Amazon SES | — | Envio de e-mails |
| Terraform | >= 1.0 | Infraestrutura como código |
| LocalStack | — | Simulação local da AWS |
| OpenTelemetry | 1.15.x | Rastreamento distribuído |
| New Relic | — | Observabilidade (logs e traces) |

---

## Pré-requisitos

### Desenvolvimento Local

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)
- [AWS CLI](https://aws.amazon.com/cli/) configurado
- [Terraform](https://www.terraform.io/downloads.html) >= 1.0
- [LocalStack](https://localstack.cloud/) rodando e acessível
- [Amazon.Lambda.Tools](https://github.com/aws/aws-extensions-for-dotnet-cli) (opcional, para deploy via CLI)

```powershell
# Instalar Amazon.Lambda.Tools globalmente
dotnet tool install -g Amazon.Lambda.TestTool-8.0
dotnet tool install -g Amazon.Lambda.Tools
```

### Produção (AWS)

- Conta AWS com permissões para: Lambda, SQS, SES, IAM, CloudWatch
- AWS CLI autenticado (`aws configure`)
- Conta no [New Relic](https://newrelic.com/) com a License Key disponível

---

## Estrutura do Projeto

```
fiap-cloudgames-notifications-lambda/
├── FiapCloudGames.Notifications.Lambda/   # Projeto principal da Lambda
│   ├── Function/
│   │   └── EmailFunction.cs               # Handler principal (entry point)
│   ├── Models/
│   │   └── EmailMessage.cs                # DTO da mensagem de e-mail
│   ├── Services/
│   │   ├── IEmailService.cs               # Interface do serviço de e-mail
│   │   ├── SesEmailService.cs             # Implementação com AWS SES
│   │   └── NewRelicLogService.cs          # Envio de logs ao New Relic
│   ├── Infrastructure/
│   │   └── SesClientFactory.cs            # Factory do cliente SES (suporte LocalStack)
│   ├── scripts/
│   │   ├── test-send-sqs-message.ps1      # Script de teste local
│   │   └── message.json                   # Payload de exemplo para SQS
│   ├── appsettings.json                   # Configurações (LocalStack por padrão)
│   └── aws-lambda-tools-defaults.json     # Defaults do deploy via dotnet lambda
├── infra/                                 # Infraestrutura Terraform
│   ├── provider.tf                        # Provider AWS com endpoints LocalStack
│   ├── main.tf                            # Recursos: IAM, SQS, Lambda, SES
│   ├── variables.tf                       # Variáveis e valores padrão
│   └── outputs.tf                         # Outputs: ARNs, URLs, comandos de teste
└── FiapCloudGames.Notifications.Lambda.slnx
```

**Handler da Lambda:**
```
FiapCloudGames.Notifications.Lambda::FiapCloudGames.Notifications.Lambda.EmailFunction::FunctionHandler
```

---

## Variáveis de Ambiente

A Lambda lê as seguintes variáveis de ambiente em tempo de execução:

| Variável | Obrigatória | Descrição | Exemplo |
|----------|-------------|-----------|---------|
| `AWS_ACCESS_KEY_ID` | Sim (LocalStack) | Chave de acesso AWS | `test` |
| `AWS_SECRET_ACCESS_KEY` | Sim (LocalStack) | Chave secreta AWS | `test` |
| `AWS_SES_ENDPOINT` | LocalStack apenas | Endpoint customizado do SES | `http://localstack:30466` |
| `AWS_REGION` | Sim | Região AWS | `us-east-1` |
| `NEW_RELIC_LICENSE_KEY` | Sim | License key do New Relic | `NRAK-...` |

> Em produção na AWS, `AWS_ACCESS_KEY_ID` e `AWS_SECRET_ACCESS_KEY` são fornecidos automaticamente pela IAM Role da Lambda. Defina apenas `AWS_REGION` e `NEW_RELIC_LICENSE_KEY`.

---

## Execução Local com LocalStack

### 1. Verificar o LocalStack

Confirme que o LocalStack está acessível na porta configurada (padrão `30466`):

```powershell
curl http://localhost:30466/_localstack/health
```

### 2. Build e empacotamento da Lambda

Execute no diretório do projeto Lambda:

```powershell
cd FiapCloudGames.Notifications.Lambda

# Compilar e publicar
dotnet publish -c Release -r linux-x64 `
  /p:GenerateRuntimeConfigurationFiles=true `
  --self-contained false `
  -o publish

# Criar o ZIP para o deploy
Compress-Archive -Path "publish\*" -DestinationPath "publish\function.zip" -Force
```

### 3. Provisionar infraestrutura com Terraform

```powershell
cd ..\infra

# Inicializar os providers
terraform init

# (Opcional) Validar a configuração
terraform validate

# Visualizar o plano de execução
terraform plan

# Aplicar a infraestrutura
terraform apply
```

Quando solicitado, confirme digitando `yes`.

**Customizando o endpoint do LocalStack:**

Se o seu LocalStack estiver em uma porta diferente, passe a variável:

```powershell
terraform apply -var="localstack_endpoint=http://localhost:4566"
```

### 4. Verificar os recursos criados

```powershell
terraform output
```

A saída inclui ARNs, URL da fila SQS e comandos prontos para teste.

### 5. Configurar New Relic (opcional para local)

Se quiser testar a integração com New Relic localmente, defina a variável de ambiente antes de executar os scripts de teste. Em LocalStack, os logs da Lambda ainda são visíveis no console sem o New Relic.

---

## Deploy na AWS (Produção)

### Opção A — Via Terraform (recomendado)

1. Configure o AWS CLI com as credenciais de produção:

```powershell
aws configure
# AWS Access Key ID: <sua-chave>
# AWS Secret Access Key: <seu-segredo>
# Default region name: sa-east-1
# Default output format: json
```

2. Edite `infra/variables.tf` (ou crie um `infra/terraform.tfvars`) apontando para a AWS real:

```hcl
# terraform.tfvars
aws_region          = "sa-east-1"
localstack_endpoint = "https://aws.amazon.com"   # não usado em prod

lambda_environment_variables = {
  AWS_REGION          = "sa-east-1"
  NEW_RELIC_LICENSE_KEY = "NRAK-XXXXXXXXXXXXXXXX"
  # Não defina AWS_SES_ENDPOINT em produção
}
```

> **Importante:** em produção remova `AWS_SES_ENDPOINT` das variáveis de ambiente da Lambda. O `SesClientFactory` detecta a ausência desse valor e usa as credenciais da IAM Role automaticamente.

3. Ajuste o `provider.tf` removendo os endpoints do LocalStack e as credenciais dummy:

```hcl
provider "aws" {
  region = var.aws_region
}
```

4. Execute o deploy:

```powershell
cd infra
terraform init
terraform apply
```

### Opção B — Via `dotnet lambda` CLI

```powershell
cd FiapCloudGames.Notifications.Lambda

# Deploy direto para AWS (usa aws-lambda-tools-defaults.json como base)
dotnet lambda deploy-function email-function `
  --function-role <ARN-da-role> `
  --region sa-east-1
```

### Verificar e-mail remetente no SES (produção)

Em produção, o SES exige que o endereço remetente seja verificado. Acesse o console da AWS → SES → Verified Identities e confirme o e-mail `no-reply@fiapcloudgames.local` (ou o endereço que você configurar).

Se estiver usando um domínio real, adicione os registros DNS retornados pelo SES no seu provedor de domínio.

---

## Testes

### Enviar mensagem de teste para a fila SQS

Use o script PowerShell incluso (a partir do diretório raiz do projeto Lambda):

```powershell
cd FiapCloudGames.Notifications.Lambda
.\scripts\test-send-sqs-message.ps1
```

O script limpa a fila e envia o payload de `scripts/message.json`:

```json
{
  "CorrelationId": "12345678-1234-1234-1234-123456789012",
  "To": "teste@email.com",
  "Subject": "Bem-vindo",
  "Body": "Sua conta foi criada"
}
```

### Enviar mensagem manualmente via AWS CLI

```powershell
aws sqs send-message `
  --queue-url http://localhost:30466/000000000000/notification-queue `
  --message-body '{"CorrelationId":"abc-123","To":"user@example.com","Subject":"Teste","Body":"Mensagem de teste"}' `
  --endpoint-url=http://localhost:30466 `
  --region us-east-1
```

### Verificar logs da Lambda no LocalStack

```powershell
aws logs tail /aws/lambda/email-function `
  --follow `
  --endpoint-url=http://localhost:30466 `
  --region us-east-1
```

### Invocar a Lambda diretamente

```powershell
aws lambda invoke `
  --function-name email-function `
  --payload '{"Records":[{"body":"{\"CorrelationId\":\"abc-123\",\"To\":\"user@example.com\",\"Subject\":\"Teste\",\"Body\":\"Teste\"}","messageId":"msg-001"}]}' `
  --endpoint-url=http://localhost:30466 `
  --region us-east-1 `
  response.json

# Ver resposta
Get-Content response.json
```

---

## Observabilidade

### Logs

A Lambda utiliza o `ILambdaContext.Logger` para logs estruturados que vão ao CloudWatch. Adicionalmente, o `NewRelicLogService` envia logs diretamente à API do New Relic (`https://log-api.newrelic.com/log/v1`) com os campos:

- `timestamp`, `message`, `level` (INFO / ERROR)
- `service` = `notification-lambda`
- `environment`
- Dados customizados: `CorrelationId`, `To`, `Subject`

### Traces (OpenTelemetry)

O rastreamento distribuído é configurado via OpenTelemetry com exporter OTLP para New Relic:

- **Endpoint:** `https://otlp.nr-data.net`
- **Protocolo:** HTTP/Protobuf
- **Source:** `notification-lambda`
- **Span:** `SendEmail` — com tag `CorrelationId`

Configure a variável `NEW_RELIC_LICENSE_KEY` para ativar o envio dos traces.

---

## Troubleshooting

### `Connection refused` ao rodar Terraform

Confirme que o LocalStack está rodando e acessível:

```powershell
curl http://localhost:30466/_localstack/health
```

Se estiver em outra porta, passe `-var="localstack_endpoint=http://localhost:PORTA"` no `terraform apply`.

### `ZIP file not found`

O arquivo `publish/function.zip` não foi gerado. Execute novamente o passo de build:

```powershell
cd FiapCloudGames.Notifications.Lambda
dotnet publish -c Release -r linux-x64 --self-contained false -o publish
Compress-Archive -Path "publish\*" -DestinationPath "publish\function.zip" -Force
```

### `Unable to locate credentials`

No LocalStack, esse aviso é esperado e já é tratado pelo `provider.tf` com `skip_credentials_validation = true`. Se aparecer na AWS real, rode `aws configure` e verifique suas credenciais.

### Lambda não dispara ao enviar mensagem na fila

Verifique se o Event Source Mapping foi criado com sucesso:

```powershell
aws lambda list-event-source-mappings `
  --function-name email-function `
  --endpoint-url=http://localhost:30466 `
  --region us-east-1
```

O campo `State` deve ser `Enabled`.

### E-mail não é enviado (SES)

Em LocalStack, o SES apenas simula o envio — não há e-mail real entregue. Verifique nos logs se o `SesEmailService` recebeu o retorno sem erro. Em produção, confirme que o endereço remetente (`no-reply@fiapcloudgames.local`) está verificado no SES.

### Logs não aparecem no New Relic

- Confirme que `NEW_RELIC_LICENSE_KEY` está correta e sem espaços.
- Verifique a conectividade de saída da Lambda para `https://log-api.newrelic.com` e `https://otlp.nr-data.net`.
- Consulte o CloudWatch Logs para erros do `NewRelicLogService`.
