# ClaimOrchestrator

A Microsoft Agent Framework workflow that checks a hospital bill with two existing Foundry prompt agents. It runs as a **Foundry hosted agent** (Responses protocol, port 8088) and can also do a one-off console run.

```
IntakeStep (intake agent) ──► ValidateStep (plain C#) ──┬─ totals match ──────────────► PolicyStep (policy agent) ──► PolicyChecked
                                                        └─ mismatch / bad JSON / null ─► StopStep ─────────────────► NeedsReview
```

- **IntakeStep** sends the bill text to the intake agent, which returns JSON.
- **ValidateStep** parses that JSON, sums `items[].amount` with `decimal` and compares it with `printedTotal`. No AI.
- Routing uses conditional edges on the C# `ValidationResult.IsValid`, never a model decision.
- **StopStep** never calls the policy agent.
- The agents are looked up by name. Their model, instructions and AI Search tool live in Foundry, not here.
- In hosted mode, `ClaimOrchestratorAgent` builds a **fresh workflow for every request**, so no state is shared between users. The latest user message is the bill text; the reply is the outcome JSON.

## Configuration

Everything is in [`appsettings.json`](appsettings.json). These environment variables override the `Foundry` section, so on another machine you usually only set these three:

| Environment variable | appsettings.json key | Required |
|---|---|---|
| `FOUNDRY_PROJECT_ENDPOINT` | `Foundry:ProjectEndpoint` | yes (injected automatically when hosted in Foundry) |
| `INTAKE_AGENT_NAME` | `Foundry:IntakeAgentName` | yes |
| `POLICY_AGENT_NAME` | `Foundry:PolicyAgentName` | yes |

Other settings:

| Key | Default | Meaning |
|---|---|---|
| `Foundry:AgentTimeoutSeconds` | 120 | Timeout for each agent call |
| `Claim:TotalTolerance` | 0.00 | Allowed difference between item sum and printed total |
| `Claim:MaxBillCharacters` | 50000 | Bills longer than this are rejected before any agent call |
| `Claim:MaxFieldLength` | 200 | Agent-extracted values are cut to this length before reuse |
| `Claim:PolicyQuestionTemplate` | see file | Question for the policy agent; `{procedure}` and `{diagnosis}` are replaced |

Authentication uses `DefaultAzureCredential`: `az login` locally, the agent's managed identity when hosted. There are no keys in code or config.

## Run locally as a hosted agent

Terminal 1, start the host on `http://localhost:8088`:

```powershell
az login
$env:FOUNDRY_PROJECT_ENDPOINT = "https://<account>.services.ai.azure.com/api/projects/<project>"
$env:INTAKE_AGENT_NAME = "<intake-agent-name>"
$env:POLICY_AGENT_NAME = "<policy-agent-name>"
$env:AZURE_TOKEN_CREDENTIALS = "dev"   # optional: skip the slow managed-identity probe locally

cd Foundry\Orchestrator\ClaimOrchestrator
dotnet run
```

Terminal 2, send a bill as the message:

```powershell
cd Foundry\Orchestrator\ClaimOrchestrator
$body = @{ input = (Get-Content samples\bill-match.txt -Raw) } | ConvertTo-Json
(Invoke-RestMethod -Uri http://localhost:8088/responses -Method Post -ContentType "application/json" -Body $body).output[0].content[0].text

$body = @{ input = (Get-Content samples\bill-mismatch.txt -Raw) } | ConvertTo-Json
(Invoke-RestMethod -Uri http://localhost:8088/responses -Method Post -ContentType "application/json" -Body $body).output[0].content[0].text
```

Expected: `bill-match.txt` → `PolicyChecked`; `bill-mismatch.txt` (items 66200.00 vs printed 68200.00) → `NeedsReview`.

If you set the folder up with `azd ai agent init` (see below), you can also use `azd ai agent run` and `azd ai agent invoke --local "<bill text>"`.

## Run once from the console

Pass a `.txt` or `.pdf` bill. PDFs have their text layer extracted, so scanned images aren't supported.

```powershell
dotnet run -- samples\pdf\hospital-bill-match.pdf      # exit code 0 = PolicyChecked
dotnet run -- samples\pdf\hospital-bill-mismatch.pdf   # exit code 2 = NeedsReview
dotnet run -- samples\bill-match.txt
dotnet run -- samples\bill-mismatch.txt
```

In Visual Studio, pick a profile (hosted agent, or one of the four demo bills) next to the ▶ button and press F5.

## Deploy to Foundry (creates Azure resources)

[`azure.yaml`](azure.yaml) and [`Dockerfile`](Dockerfile) are ready. From an empty folder outside this repository:

```powershell
azd ai agent init -m "D:\Github\MAF\Foundry\Orchestrator\ClaimOrchestrator\azure.yaml" --deploy-mode container
azd env set INTAKE_AGENT_NAME <intake-agent-name>
azd env set POLICY_AGENT_NAME <policy-agent-name>
azd deploy
```

After deployment, give the hosted agent's managed identity a role on the Foundry project that lets it read and run the intake and policy agents (for example **Azure AI User**).
