# Spam Corpus

Synthetic smoke-test fixtures, not threshold evidence. Each entry is a single submission.

The labelled copy is `demo/spam-corpus.csv`. With the test site running in Development, compare providers with the evaluation CLI:

```bash
dotnet run --project Cogworks.Umbraco.FormsGuard.Evaluation -- --provider jev,umbracoai
```

---

## Obvious spam

1. **Sales / promo / link bait**
   - Name: `John Doe`
   - Email: `discount@cheapmeds.example`
   - Message: `Buy quality discount viagra cialis online no prescription needed worldwide shipping click here cheap-pharma-deals.example.com`

2. **Cold lead-gen pitch with link**
   - Name: `Sales Pro`
   - Email: `sales@leadpro.example`
   - Message: `Hi I see you have a contact form. We can get you 100 leads/week guaranteed. Visit lead-pro-money.example.com for our special launch offer ending today!`

3. **SEO link drop**
   - Name: `Anna`
   - Email: `anna@seo-magic.example`
   - Message: `Hello, I noticed your site has potential. I can guarantee you first page Google rankings in 30 days. Reply with your number or visit rank-boost-now.example.com`

## AI-generated spam (the hard case)

1. **Generic partnership flattery**
   - Name: `Jonathan Pierce`
   - Email: `j.pierce@globalstrategy.example`
   - Message: `Dear team, I came across your website and was deeply impressed by the quality and professionalism of your services. I represent a forward-thinking partnership opportunity that I believe would align excellently with your strategic objectives. Could we schedule a brief introductory call at your earliest convenience to explore potential synergies?`

2. **Vague growth pitch**
   - Name: `Sarah Mitchell`
   - Email: `sarah@growthlabs.example`
   - Message: `Hello, your company really stood out to me as a leader in your space. I would love to connect and explore mutually beneficial opportunities that align with your goals and vision for growth. Looking forward to hearing from you.`

3. **Pattern-matched outreach**
   - Name: `Daniel`
   - Email: `daniel@bizdev.example`
   - Message: `I hope this message finds you well. After reviewing your impressive online presence, I believe there is significant potential for a strategic collaboration that could deliver substantial mutual value. I would welcome the opportunity to discuss this further at a time convenient to you.`

## Legitimate

1. **Reading-based accountancy lead**
   - Name: `Sam Patel`
   - Email: `sam@patelco.co.uk`
   - Message: `Hi, we are a 12-person accountancy in Reading. Current site is dated and we want to redevelop it on Umbraco. Could someone call to discuss approach and rough costs? Mornings work best for me.`

2. **Specific Umbraco vs Sitecore question**
   - Name: `Helen`
   - Email: `helen.b@fsiltd.example`
   - Message: `Quick question, we are evaluating Umbraco against Sitecore for a financial services client. Do you have a comparison deck or case studies for FCA-regulated builds we could review?`

3. **Existing client with a specific bug**
   - Name: `Mark`
   - Email: `mark@chartshire.org.uk`
   - Message: `Hi team, on our staging environment the donations form is throwing a 500 after the captcha step. Can someone take a look this afternoon? Project ref CHAR-2104. Cheers, Mark.`
