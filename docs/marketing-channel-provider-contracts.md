# Marketing channel provider contracts

Verified against provider documentation on 2026-08-12. These contracts describe the deliberately supported subset; a provider account or API tier may expose less.

## Shared delivery contract

- OAuth state is encrypted, company- and user-bound, expires after ten minutes, and can be consumed once. Callback URLs must be HTTPS and exactly match a provider-specific `AllowedRedirectUris` entry.
- Access and refresh tokens are stored only through the protected secret store. API responses, audit summaries, errors, and UI models contain secret references or safe health states, never credentials.
- Discovery persists only destinations the authenticated identity can manage. Removed destinations become unavailable and cannot receive new actions.
- External actions use immutable target versions, centralized policy and approval checks immediately before dispatch, a server-derived business idempotency key, concurrency-safe claims, bounded retries, cancellation, and provider lookup for reconcilable ambiguous outcomes.
- Initial adapters support organic publication. Paid media, provider-native scheduling, deletion, comments, personal-account automation, and media upload are unavailable unless a future capability contract explicitly enables them.

## LinkedIn

- Product: Community Management API. Application review and the appropriate Development or Standard tier are required. See the [Community Management overview](https://learn.microsoft.com/en-us/linkedin/marketing/community-management/community-management-overview?view=li-lms-2026-02) and [app review guide](https://learn.microsoft.com/en-us/linkedin/marketing/community-management-app-review?view=li-lms-2026-04).
- OAuth scopes requested: `openid`, `profile`, `rw_organization_admin`, `r_organization_social`, and `w_organization_social`.
- Discovery: approved organization ACL entries with a content-management role. The initial UI may display an organization URN until localized organization-name lookup is added.
- Supported action: public organization text post through the current versioned [Posts API](https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/posts-api). LinkedIn also documents image, video, document, article, poll, and multi-image forms, but this connection intentionally advertises text-only until its upload/status/reconciliation adapters are implemented and approved. Personal member publishing and provider-native scheduling are not enabled.
- Reconciliation: GET the provider post URN. Authorization and rate-limit responses are normalized; a timeout remains ambiguous and is not blindly retried.

## Meta: Facebook Pages and Instagram professional accounts

- Eligible destinations: manageable Facebook Pages and linked Instagram professional accounts. Consumer-profile automation is excluded.
- OAuth permissions requested: `pages_show_list`, `pages_read_engagement`, `pages_manage_posts`, `instagram_basic`, and `instagram_content_publish`. Meta app review and live-mode approval remain operator prerequisites.
- Supported actions: Facebook Page text publication and Instagram professional-account single-image publication using an approved public image URL. Provider-native scheduling, reels, carousels, stories, advertising, and media-binary upload are not currently advertised as capabilities.
- The Page-scoped access token returned during discovery is stored separately for its destination.
- Reconciliation: query the returned Page post, media container, or published-media identifier before classifying an ambiguous result. Provider references are never treated as successful merely because a request was attempted.
- Provider references: [Pages API posts](https://developers.facebook.com/docs/pages-api/posts/), [Instagram content publishing](https://developers.facebook.com/docs/instagram-platform/content-publishing/), and [Facebook access tokens](https://developers.facebook.com/docs/facebook-login/guides/access-tokens/).

## X

- OAuth 2.0 Authorization Code with PKCE scopes: `tweet.read`, `tweet.write`, `users.read`, and `offline.access`; see [X OAuth 2.0](https://docs.x.com/fundamentals/authentication/oauth-2-0/authorization-code).
- Discovery exposes the authenticated X account. Capabilities surface the configured access tier and an operator-visible warning that X API usage may incur provider charges.
- Supported action: text post through [Create Post](https://docs.x.com/x-api/posts/create-post). X currently documents media attachment and a separate [media upload endpoint](https://docs.x.com/x-api/media/upload-media), including tier-dependent behavior, but this connection intentionally advertises text-only until upload, processing-status, expiry, and cost/tier verification are implemented. Threads, polls, deletion, advertising, and provider-native scheduling are also not enabled.
- Reconciliation uses [Get Post by ID](https://docs.x.com/x-api/posts/get-post-by-id). A missing post becomes a justified permanent result; authorization, rate-limit, server, and unknown network outcomes retain distinct normalized states.

## Configuration checklist

For each enabled provider, configure `Marketing:ChannelOAuth:{Provider}` with `ClientId`, a protected `ClientSecretReference` where required, an honest `AccessTier`, and one or more exact `AllowedRedirectUris`. Register the same callback URI in the provider console. Keep `Marketing:ChannelDelivery:Enabled` false until credentials, permissions, destination discovery, approval policy, secret-store writes, monitoring, and reconciliation have been verified in the target environment.
