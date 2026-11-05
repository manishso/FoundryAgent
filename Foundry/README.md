# Foundry

Agents and services built on **Microsoft Agent Framework (MAF)** and **Microsoft Foundry**.

```
Foundry/
├── Foundry.sln                  # Visual Studio solution for everything in this folder
├── IntakeAgent/                 # (placeholder) Foundry prompt agent: bill text → JSON
├── McpServer/                   # (placeholder)
├── Orchestrator/
│   └── ClaimOrchestrator/       # MAF workflow, runs as a Foundry hosted agent
├── PolicyAgent/                 # (placeholder) Foundry prompt agent: coverage answers via AI Search
└── PreClaimAgent/               # (placeholder)
```

Only **ClaimOrchestrator** contains code today. The other folders are reserved for upcoming components.

The target end-to-end design (UI → Blob → Service Bus → Function → hosted agent → prompt agents and tools) is in [docs/claim-architecture.md](docs/claim-architecture.md).

---

## ClaimOrchestrator

Checks a hospital bill in three steps:

```
IntakeStep (intake agent) ──► ValidateStep (plain C#) ──┬─ totals match ──────────────► PolicyStep (policy agent) ──► PolicyChecked
                                                        └─ mismatch / bad JSON / null ─► StopStep ─────────────────► NeedsReview
```

| Step | What it does | Uses AI? |
|---|---|---|
| IntakeStep | Sends the bill text to the **intake agent**, which returns JSON (hospital, patient, diagnosis, procedure, dates, items, printedTotal) | Yes (existing Foundry prompt agent) |
| ValidateStep | Parses the JSON, sums `items[].amount` as `decimal`, compares with `printedTotal`, checks required fields | No |
| PolicyStep | Asks the **policy agent** whether the procedure is covered; outputs bill summary + answer | Yes (existing Foundry prompt agent) |
| StopStep | Outputs `NeedsReview` with the reason and both totals. The policy agent is **not** called | No |

- Routing is decided by C# conditional edges, never by a model.
- The two prompt agents must **already exist** in your Foundry project. They are looked up by name; their instructions and tools are not in this code.
- In hosted mode a fresh workflow is built for each request, so no state is shared between users.

### Prerequisites

| Tool | Install | Check |
|---|---|---|
| .NET 9 SDK or later (project targets `net9.0`) | https://dotnet.microsoft.com/download/dotnet/9.0 | `dotnet --list-sdks` |
| Visual Studio 2022 17.12+ (optional) | https://visualstudio.microsoft.com/ | Help → About |
| Azure CLI | `winget install Microsoft.AzureCLI` | `az version` |
| Azure Developer CLI (for deployment) | `winget install Microsoft.Azd` | `azd version` |
| azd AI agents extension (for deployment) | `azd extension install azure.ai.agents` | `azd extension list` |

You also need:
- A Microsoft Foundry project, and its endpoint (`https://<account>.services.ai.azure.com/api/projects/<project>`).
- The intake and policy prompt agents created in that project.
- A role on the project that can read and run agents, for example **Azure AI User**.

### Configuration

All settings are in [`Orchestrator/ClaimOrchestrator/appsettings.json`](Orchestrator/ClaimOrchestrator/appsettings.json). On a new machine you normally only set these three environment variables, which override the file:

| Environment variable | appsettings.json key | Notes |
|---|---|---|
| `FOUNDRY_PROJECT_ENDPOINT` | `Foundry:ProjectEndpoint` | Injected automatically when hosted in Foundry |
| `INTAKE_AGENT_NAME` | `Foundry:IntakeAgentName` | Name of the intake prompt agent |
| `POLICY_AGENT_NAME` | `Foundry:PolicyAgentName` | Name of the policy prompt agent |

Other settings (timeout, total tolerance, size limits, policy question template) are described in the [ClaimOrchestrator README](Orchestrator/ClaimOrchestrator/README.md#configuration).

If any of the three is missing, the app stops with a clear error. Authentication uses `DefaultAzureCredential` (`az login` locally, managed identity when hosted). There are no keys in code or config.

### Build

```powershell
cd D:\Github\MAF\Foundry
dotnet build Foundry.sln
```

To open in Visual Studio: **File → Open → Project/Solution →** `Foundry\Foundry.sln`.

### Run locally as a hosted agent

**Terminal 1** starts the agent host on `http://localhost:8088`, using the Responses protocol:

```powershell
az login
$env:FOUNDRY_PROJECT_ENDPOINT = "https://<account>.services.ai.azure.com/api/projects/<project>"
$env:INTAKE_AGENT_NAME = "<intake-agent-name>"
$env:POLICY_AGENT_NAME = "<policy-agent-name>"
$env:AZURE_TOKEN_CREDENTIALS = "dev"   # optional: skips the slow managed-identity probe locally

cd D:\Github\MAF\Foundry\Orchestrator\ClaimOrchestrator
dotnet run
```

Wait for `Now listening on: http://[::]:8088` (or similar).

**Terminal 2** sends a bill. The bill text is the message:

```powershell
cd D:\Github\MAF\Foundry\Orchestrator\ClaimOrchestrator

# Totals match -> PolicyChecked
$body = @{ input = (Get-Content samples\bill-match.txt -Raw) } | ConvertTo-Json
(Invoke-RestMethod -Uri http://localhost:8088/responses -Method Post -ContentType "application/json" -Body $body).output[0].content[0].text

# Totals differ (66200.00 vs 68200.00) -> NeedsReview
$body = @{ input = (Get-Content samples\bill-mismatch.txt -Raw) } | ConvertTo-Json
(Invoke-RestMethod -Uri http://localhost:8088/responses -Method Post -ContentType "application/json" -Body $body).output[0].content[0].text
```

Health check: `Invoke-WebRequest http://localhost:8088/readiness` returns `200`.

Example `PolicyChecked` reply:

```json
{
  "status": "PolicyChecked",
  "reason": null,
  "itemsTotal": 65150.50,
  "printedTotal": 65150.50,
  "bill": { "hospital": "CITY CARE HOSPITAL", "patient": "Rahul Sharma", "procedure": "Laparoscopic appendectomy", "...": "..." },
  "policyAnswer": "<answer from the policy agent>"
}
```

Example `NeedsReview` reply:

```json
{
  "status": "NeedsReview",
  "reason": "Sum of items (66200.00) does not match printed total (68200.00); difference 2000.00.",
  "itemsTotal": 66200.00,
  "printedTotal": 68200.00,
  "bill": null,
  "policyAnswer": null
}
```

### Run once from the console (no host)

The console mode takes one bill file, either **`.txt` or `.pdf`**. For a PDF, its text layer is extracted with PdfPig. Scanned, image-only PDFs aren't supported.

Demo bills:

| File | Items sum | Printed total | Expected |
|---|---|---|---|
| `samples/pdf/hospital-bill-match.pdf` | 65150.50 | 65150.50 | PolicyChecked |
| `samples/pdf/hospital-bill-mismatch.pdf` | 66200.00 | 68200.00 | NeedsReview |
| `samples/bill-match.txt` | 65150.50 | 65150.50 | PolicyChecked |
| `samples/bill-mismatch.txt` | 66200.00 | 68200.00 | NeedsReview |

With the same environment variables set:

```powershell
cd D:\Github\MAF\Foundry\Orchestrator\ClaimOrchestrator
dotnet run -- samples\pdf\hospital-bill-match.pdf      # exit code 0 = PolicyChecked
dotnet run -- samples\pdf\hospital-bill-mismatch.pdf   # exit code 2 = NeedsReview (1 = error)
dotnet run -- samples\bill-match.txt
dotnet run -- samples\bill-mismatch.txt
```

PDF limits are in `appsettings.json`: `Claim:MaxBillFileBytes` (default 10 MB) and `Claim:MaxPdfPages` (default 20).

### Run from Visual Studio

1. Open `Foundry\Foundry.sln`.
2. Put your values in `Orchestrator\ClaimOrchestrator\appsettings.json` (`Foundry:ProjectEndpoint`, `IntakeAgentName`, `PolicyAgentName`), or set the three environment variables in Windows and restart Visual Studio.
3. Sign in to Azure in Visual Studio (**Tools → Options → Azure Service Authentication**), or run `az login` in a terminal.
4. Choose a launch profile in the dropdown next to the green ▶ button, then press **F5**:

| Profile | What it runs |
|---|---|
| Hosted agent (port 8088) | Starts the hosted agent; send bills with `Invoke-RestMethod` as shown above |
| Console - PDF bill (totals match) | `samples\pdf\hospital-bill-match.pdf` → PolicyChecked |
| Console - PDF bill (totals differ) | `samples\pdf\hospital-bill-mismatch.pdf` → NeedsReview |
| Console - TXT bill (totals match) | `samples\bill-match.txt` → PolicyChecked |
| Console - TXT bill (totals differ) | `samples\bill-mismatch.txt` → NeedsReview |

The profiles are defined in [`Properties/launchSettings.json`](Orchestrator/ClaimOrchestrator/Properties/launchSettings.json). The `samples` folder is copied to the build output, so relative paths work whatever working directory Visual Studio uses. To try your own bill, add a profile with `"commandLineArgs": "C:\\path\\to\\bill.pdf"`.

### Host in Foundry (deploy)

> These commands create Azure resources (container registry, hosted agent) and may incur cost.

[`azure.yaml`](Orchestrator/ClaimOrchestrator/azure.yaml) and the [`Dockerfile`](Orchestrator/ClaimOrchestrator/Dockerfile) are ready. Run the following from an **empty folder outside this repository**:

```powershell
az login
azd auth login

mkdir C:\agents\claim-orchestrator; cd C:\agents\claim-orchestrator
azd ai agent init -m "D:\Github\MAF\Foundry\Orchestrator\ClaimOrchestrator\azure.yaml" --deploy-mode container

azd env set INTAKE_AGENT_NAME <intake-agent-name>
azd env set POLICY_AGENT_NAME <policy-agent-name>

# Optional: test through azd before deploying
azd ai agent run
azd ai agent invoke --local "<bill text>"

# Deploy to Foundry
azd deploy

# Invoke the deployed agent
azd ai agent invoke "<bill text>"
```

After the first deployment, give the hosted agent's **managed identity** a role on the Foundry project that lets it read and run the intake and policy agents, for example **Azure AI User**. Without it, the agent starts but every bill fails at IntakeStep.

The deployed agent also appears in the Foundry portal, where you can test it from the agent playground.

### Troubleshooting

| Problem | Fix |
|---|---|
| `Configuration error: ... is not set` | Set the three environment variables in the same terminal before `dotnet run`. |
| `Step IntakeStep failed: ... 401/403` | Run `az login` with an account that has a role (for example Azure AI User) on the Foundry project. |
| `Step IntakeStep failed: ... 404` / agent not found | Check `INTAKE_AGENT_NAME` / `POLICY_AGENT_NAME` match the agent names in Foundry exactly. |
| Every bill returns `NeedsReview` with "not valid JSON" | The intake agent is returning extra text around the JSON. It must return JSON only (one ```` ```json ```` fence is tolerated). |
| Slow first call | `DefaultAzureCredential` is probing credentials. Set `$env:AZURE_TOKEN_CREDENTIALS = "dev"` locally. |
| Port 8088 already in use | Stop the other process, or another hosted agent (for example `SensorOrchestratorAgent`) running on the same port. |
| Bill rejected as too long | Raise `Claim:MaxBillCharacters` in `appsettings.json`. |
| `PDF has no extractable text` | The PDF is a scanned image. Use a PDF with a text layer, or add OCR first. |
| `DefaultAzureCredential failed ... needs re-authentication` | Re-sign in under Visual Studio **Tools → Options → Azure Service Authentication**, or run `az login`. |
| Visual Studio can't load the project | The project targets `net9.0`; use Visual Studio 2022 17.12 or later. |
