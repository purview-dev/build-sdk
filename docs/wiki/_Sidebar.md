<!-- Page order for the documentation site. purview.dev reads these links in document order to order the published
     sidebar, so every page appears here exactly once, grouped by section and in reading order. Paths are relative to
     this file at the wiki root, which is also what the sync slugifies: `packaging/SourceLink.md` is published at
     /docs/build-sdk/packaging/sourcelink/. Keep the labels in step with the matching `## ` headings in Home.md;
     DocumentationWikiTests enforces both rules. -->

- [Home](Home.md)
- **Get started**
  - [Getting Started](Getting-Started.md)
  - [Repository Bootstrap](Repository-Bootstrap.md)
  - [Engineering Principles](concepts/Engineering-Principles.md)
- **Conventions and detection**
  - [Project Naming Conventions](concepts/Project-Naming-Conventions.md)
  - [Project Type Detection](concepts/Project-Type-Detection.md)
  - [Assembly Name Generation](concepts/Assembly-Name-Generation.md)
- **Configuration**
  - [Configuration Reference](Configuration-Reference.md)
  - [Property Schema and IntelliSense](configuration/Property-Schema.md)
  - [Version Detection](configuration/Version-Detection.md)
  - [InternalsVisibleTo](configuration/InternalsVisibleTo.md)
- **Testing**
  - [Testing Wiring](testing/Testing-Wiring.md)
- **Analyzers and code fixes**
  - [SDK-Shipped Analyzers](analyzers/Analyzers.md)
  - [Code Fixes](analyzers/Code-Fixes.md)
- **Packaging**
  - [Packaging](packaging/Packaging.md)
  - [SourceLink](packaging/SourceLink.md)
- **Agent skills and tooling**
  - [Agent Skills](tooling/Agent-Skills.md)
  - [Justfile](tooling/Justfile.md)
- **Contributing and releases**
  - [Release Flow](development/Release-Flow.md)
  - [Performance](development/Performance.md)
