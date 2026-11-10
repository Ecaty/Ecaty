# Distribution and licensing findings

Checked 2026-11-06. Engineering assessment, not store approval or a legal opinion. Recheck the applicable terms before release; model location does not remove publisher responsibility.

## Stores

| Destination | Finding and consequence |
|---|---|
| Steam | AI-created player content is covered by the content survey. Runtime generation needs a description of guardrails against illegal content; pre-generated content has disclosure/IP requirements too. Local inference is still live generation. Approval is case-specific. |
| Epic Games Store | Epic's content/IP rules apply. No blanket AI acceptance promise is established by the sources checked. Confirm EGS-specific distribution requirements and the agreement before submission; Fab/UEFN policies are different products. |
| Google Play | AI-generation apps must prevent restricted content and provide an in-app way to flag/report offensive content. This is not only a multiplayer requirement. |
| Apple App Store | Recording needs consent and a clear indicator. Rule 2.5.2 restricts downloading/executing code that changes functionality; 4.7 provides a conditional route for some mini-apps/games. Calling Flecs Script "data" does not guarantee exemption. Submit a clear explanation of the bounded, shipped runtime and generated recipes; review may depend on product details. Mobile must embed compiled inference libraries, not download/run Windows executables. |

Sources: [Steam content survey](https://partner.steamgames.com/doc/gettingstarted/contentsurvey), [Epic content rules](https://legal.epicgames.com/epicgames/content-guidelines), [EGS requirements](https://dev.epicgames.com/docs/epic-games-store/requirements-guidelines/distribution-requirements/requirements-overview), [Google AI policy](https://support.google.com/googleplay/android-developer/answer/13985936?hl=en), [Apple review rules](https://developer.apple.com/app-store/review/guidelines/) (2.5.2, 2.5.14, 4.7).

Steam's current external-AI-service FAQ also discusses managing player access and Steam-supported payment. Our earlier bring-your-own-cloud-subscription approach must be checked with Valve before a Steam release; do not assume it bypasses those requirements. Fully local inference avoids a metered external service but not content obligations.

## Unity and the "engine" question

Unity licenses the Runtime embedded in projects, but current section 2 expressly warns that service providers, platforms and other non-first-party creator uses may require separate rights/written authorization. Its Platform definition also covers some third-party content aggregation/distribution and AI conversion services. Therefore a creative game with saved recipes is not automatically licensed the same way as a standalone engine/UGC platform.

Before commercialization, obtain written Unity clarification for our actual creation/sharing model, supported platforms, any user-experience monetization, and any export/build/hosting route. We are not currently exposing the Unity Editor or building standalone native apps on users' behalf. Naming the app a game does not settle the contract. [Unity Software Terms](https://unity.com/legal/editor-terms-of-service/software), sections 2, 2.1, 2.2 and definitions.

## Assets, models and user content

Check each dependency's actual license and notices, including weights/quantizations/projectors, SoundFonts and redistribution permissions. llama.cpp/whisper.cpp runtime code uses MIT; selected Qwen model is Apache-2.0. These licenses do not guarantee that every generated output is copyrightable or non-infringing. Users must have rights to uploaded images; provide takedown/reporting for public sharing. Paid Unity plugins remain compiled project dependencies, not extractable source/tools for downstream users; inspect their particular licenses before export features.

Keep camera/microphone consent, local/cloud disclosure, retention and child-user privacy in the release design. Local audio is not deliberately saved by the app; optional developer traces can contain prompts/images/tools and must stay opt-in/private. A user's own AI login does not transfer all product responsibilities to that provider.

## Release gates

Validate recipes and tool permissions, enforce resource budgets, add output/content guardrails and reporting before public distribution. Later sharing adds moderation, block/report, permissions, provenance, age controls and safe shared-recipe updates. Model instructions alone are not a security boundary. These systems are backlog items, not completed protections.


## Android runtime scripts

Google Play permits qualifying interpreted code loaded at runtime. Package the compiled Flecs engine, Unity adapters and native AI libraries in the Play build. Keep generated scripts within controlled app capabilities and deliver new compiled capabilities through Play updates. This is an assessment of the architecture under the interpreter exception. The shipped app still needs the usual content, privacy and security checks. [Device and Network Abuse policy](https://support.google.com/googleplay/android-developer/answer/16559646?hl=en)

## itch.io, checked 2026-11-07

itch.io reported deleting a large quantity of AI spam on November 3. Its published policy permits useful AI-generated content and allows the platform to remove mass-produced, minimally curated or repetitive uploads from discovery. A discovery removal hides a page from browsing and search. The November post also describes deletion, but gives no inventory of affected projects. [Announcement](https://bsky.app/profile/itch.io/post/3mzev6ghw5s2x) · [Creator quality guidelines](https://itch.io/docs/creators/quality-guidelines#avoid-uploading-excessive-amounts-of-automatically-generated-or-ai-generated-content)

Ecaty should disclose **Code** where shipped code includes generated contributions. The form includes that category even when a project ships no generated art. The listing should also explain optional live generation and how users can create manually once that workflow ships. The published rules do not specifically map an optional live generator to a separate disclosure field. Disclose any shipped generated images, sound or text under their applicable categories. Generated concept art used only internally does not become shipped content. [Disclosure announcement](https://itch.io/t/4309690/generative-ai-disclosure-tagging) · [Current guidelines](https://itch.io/docs/creators/quality-guidelines#ai-disclosure)

A curated creation tool can be listed under these rules. The listing remains subject to ordinary moderation and users' AI filters. Mass publishing near-identical generated projects or asset packs would create a separate spam concern.
