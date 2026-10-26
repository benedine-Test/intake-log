# Intake Log

A calorie, protein, exercise and weight tracker built for quick logging of Singapore food. It's a Blazor WebAssembly PWA on .NET 10: it runs entirely in the browser, installs to an iPhone home screen, works offline, and hosts free on GitHub Pages.

<img src="docs/today.png" alt="Today screen" width="320">

## Features

- **One-tap food logging**: about 55 Singapore staples (chicken rice, yong tau foo, kopi-O kosong, bubble tea), plus your recent and saved foods first. Half-portion steps.
- **AI food search** (optional): ask Claude about any food not on the list and get calories, protein, carbs, fat, fibre, sugar and sodium for a typical Singapore serving. Uses your own Claude API key, entered under **My plan → AI food search**; it is stored only in this browser and left out of backups. Foods you add from AI results are saved to My foods, so repeats are free.
- **Daily panel** styled like a nutrition label: calories left, protein against target, carbs, fat, water, active minutes.
- **Exercise** with no-equipment presets (HDB stairs, brisk walk, bodyweight circuit). Calories are estimated from MET values, or you can enter your watch's number. Only half is added back to your budget.
- **Suggestions** based on the day: how long to walk off an overage, low protein, no movement yet, weekly 150-minute target, plateau warning.
- **Trends**: 7-day average weight against goal, 14-day calorie bars, logging streak.
- **Plan**: Mifflin-St Jeor BMR and TDEE, target held at a 1,200 kcal (female) or 1,500 kcal (male) floor, estimated goal date.
- **Backup**: export and import all data as JSON.

## Where data lives

Everything is stored in the browser's `localStorage` on one device; there is no server. Use **My plan → Export backup** regularly and **Import backup** to move devices.

On iPhone, add the app to the Home Screen (Safari → Share → Add to Home Screen). Safari can clear storage for websites you haven't visited in a while, and installed web apps are treated more like apps.

## Project layout

```
IntakeLog.slnx
├─ src/IntakeLog.Core          Models, calculations, presets, suggestions (no UI, fully testable)
├─ src/IntakeLog.Web           Blazor WebAssembly PWA
│  ├─ Pages/                   Home (Today), Trends, PlanPage
│  ├─ Components/              FactsPanel, WeightChart, CalorieChart, StateComponent
│  ├─ Services/AppState.cs     In-memory state + localStorage persistence
│  └─ wwwroot/js/interop.js    localStorage and file download helpers
├─ tests/IntakeLog.Core.Tests  xUnit tests for calculations and insights
└─ .github/workflows/deploy.yml  Test, publish and deploy to GitHub Pages
```

## Run locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet test
dotnet run --project src/IntakeLog.Web
```

Then open the URL it prints. Open the folder in Visual Studio or Rider via `IntakeLog.slnx`.

## Deploy to GitHub Pages

1. Push this repository to GitHub with `main` as the default branch.
2. In the repository, go to **Settings → Pages** and set **Source** to **GitHub Actions**.
3. Push to `main` (or run the workflow manually from the **Actions** tab).

The workflow runs the tests, sets the base path to `/<repo-name>/`, publishes, and deploys. The site appears at `https://<username>.github.io/<repo-name>/`.

## Changing the numbers

- AI food search prompt, model and response format: `src/IntakeLog.Core/AiFoodSearch.cs`
- Food presets and activities: `src/IntakeLog.Core/Presets.cs`
- Formulas, calorie floor, exercise credit share: `src/IntakeLog.Core/Calculator.cs`
- Suggestion rules and plateau detection: `src/IntakeLog.Core/Insights.cs`

## Caveats

Hawker calorie values are typical-portion estimates and vary by stall. BMR and exercise burn are formula estimates. Adjust your target after 3–4 weeks based on your weight trend. This app isn't medical advice.
