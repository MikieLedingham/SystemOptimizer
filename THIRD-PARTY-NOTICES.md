# Third-party notices

System Optimizer is MIT licensed (see `LICENSE`). It uses the following
third-party components, each under its own licence.

| Component | Licence | Used for |
|---|---|---|
| **.NET 10 runtime**, WPF and Windows Forms | MIT | The application runs on it, and **ships it** — see below |
| **MaterialIcons-Regular.ttf** (Google Material Icons) | Apache License 2.0 | Every icon glyph in the overlay, and the message-box icons |
| **Newtonsoft.Json** 13.0.3 | MIT | Preferences, cleanup manifests and Sanity Check state |
| **System.Management** 10.0.10 | MIT | WMI queries — system statistics, Diagnostics, and several Sanity Check probes |

That is the whole list. The application has exactly two `PackageReference`s and
one embedded font.

## The runtime is redistributed, not just used

Worth stating plainly because it is easy to miss: the published build is
`SelfContained` and `PublishSingleFile`, so the ~67 MB executable **contains a
copy of the .NET runtime, WPF and Windows Forms**. The product does not merely
require .NET — it distributes it. That is expressly permitted (.NET is MIT
licensed by Microsoft) and it is why a user needs nothing installed to run this,
but it is a redistribution and belongs in these notices.

## Removed — recorded deliberately

**Extended.WPF.Toolkit (Xceed) 4.7** was once referenced and its assemblies
were embedded in the shipped executable. It ships under the **Xceed Community
License**, which permits **non-commercial use only**, caps distribution at fewer
than 10 end-users, forbids accepting donations, and requires a visible Xceed
copyright notice in the resulting work.

Not one line of source ever referenced it — it was a dead project reference that
Costura embedded anyway (18 assemblies, ~0.63 MB compressed). **It has been
removed**, which is what makes the MIT licence grantable. This note exists so the
situation is not accidentally recreated.

**Ink Free** (`Inkfree.ttf`) was bundled as an application resource. It is a
Microsoft system font and is not redistributable. Removed, and replaced with the
system UI font.

**Aptos** was considered as the application font and rejected on the same
ground: it ships with Microsoft 365 rather than Windows, and is not
redistributable. The application uses whatever Windows already provides —
Segoe UI Variable Text, falling back to Segoe UI — and embeds no text font.

**Removed during the 2.0 rebuild**, and listed here because these notices went on
naming them for two phases after they were gone: `WpfAnimatedGif` (Apache 2.0),
`Hardware.Info` (MIT), `Costura.Fody` and `Fody` (MIT), `ILRepack.Lib.MSBuild`
(Apache 2.0), `Microsoft.Toolkit.Uwp.Notifications` (MIT), and
`Microsoft.Windows.Compatibility` with the ~60 `System.*` packages it dragged in.
None of them is in the product.

---

*Adding a dependency means adding it here, and checking its licence is compatible
with MIT redistribution first. **Removing one means deleting it from here in the
same commit** — this file spent two phases describing components that had already
gone, which is the failure that is easy to miss precisely because nothing breaks.*
