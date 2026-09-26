# fiap-esperanca-solidaria-notificacao-lambda

Lambda de **envio de e-mails transacionais** da plataforma Esperança Solidária (FIAP 11NETT). É acionada
por mensagens na fila SQS `notification-queue`, envia o e-mail via Amazon SES e registra logs/traces no
New Relic.

> A infraestrutura (fila, Lambda, trigger, identidade SES) é provisionada pelo Terraform do repositório
> **`fiap-esperanca-solidaria-infra`** (`terraform/k8s` ou `terraform/docker-compose`). Para subir o
> ambiente inteiro, siga o README de lá.

---

## Sumário

- [Visão geral](#visão-geral)
- [Stack](#stack)
- [Estrutura do projeto](#estrutura-do-projeto)
- [Contrato da mensagem](#contrato-da-mensagem)
- [Variáveis de ambiente](#variáveis-de-ambiente)
- [Build e deploy](#build-e-deploy)
- [Testando](#testando)
- [Observabilidade](#observabilidade)
- [Troubleshooting](#troubleshooting)
- [Pendências](#pendências)

---

## Visão geral

```
usuario-api (EmailNotificationPublisher)
        │  { To, Subject, Body, CorrelationId }
        ▼
SQS notification-queue ──(event source mapping, batch 1)──▶ Lambda email-function
                                                              │ 1. desserializa EmailMessage
                                                              │ 2. span OpenTelemetry "SendEmail"
                                                              │ 3. SesEmailService.SendAsync
                                                              │ 4. log INFO/ERROR no New Relic
                                                              ▼
                                                   Amazon SES (simulado no LocalStack)
```

Recursos criados pelo Terraform da infra (`terraform/k8s/main.tf`):

| Recurso | Nome |
|---|---|
| SQS | `notification-queue` |
| Lambda | `email-function` (runtime `dotnet8`, pacote `terraform/lambda-notification/function.zip`) |
| Event source mapping | `notification-queue` → `email-function`, batch size 1 |
| SES identity | `no-reply@fiapcloudgames.local` (variável `ses_verified_email`) |
| IAM | role/policy com permissão de SQS, SES e logs |

---

## Stack

| Tecnologia | Uso |
|---|---|
| .NET 8 | Runtime da Lambda |
| Amazon.Lambda.Core / SQSEvents / Serialization.SystemTextJson | Handler SQS |
| AWSSDK.SimpleEmail | Envio via SES |
| OpenTelemetry (OTLP HTTP/Protobuf + console) | Traces para o New Relic |
| New Relic Log API | Logs estruturados |

---

## Estrutura do projeto

```
.
├── FiapEsperancaSolidaria.Notifications.Lambda.slnx
└── FiapEsperancaSolidaria.Notifications.Lambda/
    ├── Function/EmailFunction.cs          # ★ Handler: FunctionHandler(SQSEvent, ILambdaContext)
    ├── Models/EmailMessage.cs             # DTO da mensagem (To, Subject, Body, CorrelationId)
    ├── Services/
    │   ├── IEmailService.cs
    │   ├── SesEmailService.cs             # Monta o SendEmailRequest (remetente fixo no código)
    │   └── NewRelicLogService.cs          # POST em https://log-api.newrelic.com/log/v1
    ├── Infrastructure/SesClientFactory.cs # Cliente SES: endpoint customizado (LocalStack) ou padrão (IAM role)
    ├── scripts/
    │   ├── test-send-sqs-message.ps1      # Limpa a fila e envia scripts/message.json
    │   └── message.json                   # Payload de exemplo
    ├── aws-lambda-tools-defaults.json     # Defaults do `dotnet lambda` (handler, runtime, memória, timeout)
    ├── Properties/launchSettings.json     # Perfil do Mock Lambda Test Tool (porta 5051)
    ├── appsettings*.json                  # Não usados pelo código (a configuração vem só de env vars)
    ├── trust-policy.json                  # Trust policy de exemplo para a role da Lambda
    ├── function.zip                       # Último pacote gerado localmente
    └── response.json                      # Saída antiga de um `aws lambda invoke` (artefato de teste)
```

**Handler:**

```
FiapEsperancaSolidaria.Notifications.Lambda::FiapEsperancaSolidaria.Notifications.Lambda.EmailFunction::FunctionHandler
```

---

## Contrato da mensagem

Corpo JSON de cada mensagem SQS (nomes em PascalCase, como o `usuario-api` publica):

```json
{
  "CorrelationId": "12345678-1234-1234-1234-123456789012",
  "To": "destinatario@exemplo.com",
  "Subject": "Bem-vindo",
  "Body": "Sua conta foi criada"
}
```

O corpo do e-mail é enviado como texto puro. O remetente é `no-reply@fiapcloudgames.local`, fixo em
`SesEmailService.cs`.

---

## Variáveis de ambiente

A Lambda lê **apenas variáveis de ambiente** (`SesClientFactory`):

| Variável | Obrigatória | Descrição | Valor padrão no Terraform |
|---|---|---|---|
| `AWS_SES_ENDPOINT` | só LocalStack | Endpoint do SES; se ausente usa o cliente padrão (AWS real + IAM role) | `http://host.docker.internal:4566` |
| `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` | só LocalStack | Credenciais usadas com endpoint customizado | `test` / `test` |
| `AWS_REGION` | sim | Região | `us-east-1` |
| `NEW_RELIC_LICENSE_KEY` | opcional | License key para logs e traces no New Relic | (não definida) |

No Terraform da infra essas variáveis vêm de `lambda_environment_variables` (`terraform/k8s/variables.tf`).
Não commite a license key do New Relic; passe-a via `terraform.tfvars` (não versionado).

---

## Build e deploy

Pré-requisitos: .NET SDK 8 e, opcionalmente, `Amazon.Lambda.Tools`
(`dotnet tool install -g Amazon.Lambda.Tools`).

```powershell
cd FiapEsperancaSolidaria.Notifications.Lambda

# opção 1: Amazon.Lambda.Tools
dotnet lambda package -c Release -o function.zip

# opção 2: publish + zip
dotnet publish -c Release -r linux-x64 --self-contained false -o publish
Compress-Archive -Path publish\* -DestinationPath function.zip -Force
```

Copie o pacote para o repo de infra e aplique o Terraform:

```powershell
Copy-Item function.zip ..\..\fiap-esperanca-solidaria-infra\terraform\lambda-notification\function.zip -Force
cd ..\..\fiap-esperanca-solidaria-infra\terraform\k8s
terraform apply
```

O `source_code_hash` do Terraform detecta o zip novo e atualiza a função. Commite o `function.zip`
atualizado no repo de infra.

---

## Testando

Com o ambiente k8s no ar (LocalStack em `localhost:30466`):

```powershell
cd FiapEsperancaSolidaria.Notifications.Lambda
.\scripts\test-send-sqs-message.ps1     # purge da fila + envio de scripts/message.json
```

Ou manualmente:

```powershell
aws sqs send-message --endpoint-url http://localhost:30466 --region us-east-1 `
  --queue-url http://localhost:30466/000000000000/notification-queue `
  --message-body '{"CorrelationId":"abc-123","To":"user@example.com","Subject":"Teste","Body":"Mensagem de teste"}'
```

Invocação direta (sem passar pela fila):

```powershell
aws lambda invoke --function-name email-function --endpoint-url http://localhost:30466 --region us-east-1 `
  --payload '{"Records":[{"messageId":"msg-001","body":"{\"CorrelationId\":\"abc\",\"To\":\"user@example.com\",\"Subject\":\"Teste\",\"Body\":\"Teste\"}"}]}' `
  --cli-binary-format raw-in-base64-out response.json
```

Logs e e-mails "enviados" no LocalStack:

```powershell
kubectl exec -n localstack deploy/localstack -- awslocal logs tail /aws/lambda/email-function
curl http://localhost:30466/_aws/ses      # mensagens capturadas pelo SES simulado
```

Para depurar localmente, use o perfil **Mock Lambda Test Tool** do `launchSettings.json`
(`dotnet tool install -g Amazon.Lambda.TestTool-8.0`).

---

## Observabilidade

- **Logs**: `ILambdaContext.Logger`/`LambdaLogger` (CloudWatch/LocalStack) e, em paralelo, New Relic Log
  API com `service = notification-lambda`, nível, `CorrelationId`, destinatário, assunto e duração.
- **Traces**: `ActivitySource` `notification-lambda`, span `SendEmail` com a tag `CorrelationId`, exportado
  para o console e via OTLP para `https://otlp.nr-data.net`.

---

## Troubleshooting

| Sintoma | Solução |
|---|---|
| Mensagem na fila não dispara a Lambda | Confira o mapping: `aws lambda list-event-source-mappings --function-name email-function --endpoint-url http://localhost:30466` (estado `Enabled`). |
| Erro de conexão com o SES | `AWS_SES_ENDPOINT` precisa ser alcançável de dentro do container da Lambda no LocalStack. |
| `[NewRelic] Log status: Forbidden/Unauthorized` | `NEW_RELIC_LICENSE_KEY` ausente ou inválida — não afeta o envio do e-mail. |
| Nenhum e-mail chega | Esperado no LocalStack (SES é simulado); veja `/_aws/ses`. Na AWS real o remetente precisa estar verificado no SES. |
| `function.zip` não encontrado no Terraform | Gere o pacote e copie para `infra/terraform/lambda-notification/`. |

---

## Pendências

- O `usuario-api` tem o publisher pronto, mas a chamada no cadastro está comentada; hoje nada publica na
  `notification-queue` no fluxo normal.
- Falhas no envio são capturadas e apenas logadas — a mensagem é confirmada mesmo assim (sem retry/DLQ) e
  o log "Email enviado" é emitido também nos casos de erro.
- O remetente (`no-reply@fiapcloudgames.local`) é herdado do projeto anterior e está fixo no código.
- Os `appsettings*.json` não são lidos pelo código; podem ser removidos ou passar a ser usados.
