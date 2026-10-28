import { LegalContact } from "@/components/Reelshelf/LegalPage";
import { legalOperator } from "./legal";

/**
 * The privacy policy, kept whole so it reads as one document. When it changes, change `updated` too
 * (and see the note on the privacy policy in AGENTS.md).
 */
export const privacyCopy = {
  title: "Privacy policy",
  updated: "26 October 2026",
} as const;

export function PrivacyText() {
  return (
    <>
      <p>
        This explains what Reelshelf, run by {legalOperator}, collects, why, and
        who else handles it. We don't sell your data, show ads or use tracking
        or analytics scripts.
      </p>

      <h2>What we collect</h2>
      <ul>
        <li>
          <strong>From Discord or Twitch when you sign in:</strong> your account
          ID, username, display name, avatar, and email address if verified. You
          can change or clear the email in Settings.
        </li>
        <li>
          <strong>Twitch access tokens</strong>, stored encrypted, so we can
          list and import your own Twitch clips when you ask us to.
        </li>
        <li>
          <strong>What you add:</strong> clips and their titles, tags, games,
          collections, collaborators and share links.
        </li>
        <li>
          <strong>If you subscribe to a paid plan:</strong> your plan, whether
          it's active, when it renews or ends, and the IDs Stripe gives your
          customer record and subscription. Your card or other payment details
          go to Stripe and never reach us.
        </li>
        <li>
          <strong>Your access:</strong> your role, any plan we give you without
          charge, whether your account is suspended, and a record of each change
          to any of them: when, which of us made it and why.
        </li>
        <li>
          <strong>If you report a shared clip:</strong> the reason you picked,
          anything you wrote, and your email address if you gave it, so we can
          tell you the outcome or ask for more. We don't link the report to your
          account. The clip's owner is never told who reported it.
        </li>
        <li>
          <strong>Usage within Reelshelf:</strong> which clips you've watched,
          so we can mark them as seen, and when you added each clip in the last
          30 days, for your plan's limit on new clips (kept even if you delete
          the clip, and forgotten after 30 days).
        </li>
        <li>
          <strong>Server logs:</strong> our hosting keeps standard request logs
          (such as IP address and browser) for security and debugging.
        </li>
      </ul>

      <h2>Cookies and local storage</h2>
      <p>
        We use one cookie to keep you signed in, and your browser's local
        storage to remember your light or dark theme and whether you've signed
        in on that browser before, so the front page can load without waiting to
        check. Nothing is used for advertising or tracking.
      </p>

      <h2>Automated analysis of clips</h2>
      <ul>
        <li>
          <strong>Apex Legends detection:</strong> for Apex clips, still frames
          are sent to OpenAI to recognise which legend you're playing, so clips
          can be sorted by legend. To compare how well models do this, we may
          also send the same frames to Anthropic, or through OpenRouter to the
          model providers behind it.
        </li>
        <li>
          <strong>Speech transcription:</strong> for accounts on the Pro plan
          (and a few others), a clip's audio is sent through OpenRouter to a
          speech-to-text provider (such as Google or Deepgram) to make clips
          searchable. Transcripts are used for search and may be reviewed by us
          to check their quality. Email us if you'd like your clips left out of
          transcription.
        </li>
        <li>
          <strong>Clip summaries:</strong> a transcript, with the clip's title,
          game and date, is sent to OpenAI to write a short searchable
          description of the clip, and that description is turned into a search
          index entry. Summaries are stored, like transcripts, for search and
          quality checks, and are deleted with the clip.
        </li>
      </ul>
      <p>
        These providers receive the data only to return a result to us, and
        handle it under their own API terms.
      </p>

      <h2>Who else handles your data</h2>
      <ul>
        <li>
          <strong>Bunny.net</strong> stores and streams your clips. We are
          moving clips to our own pipeline, existing ones included, so some
          clips are instead stored and delivered by <strong>Cloudflare</strong>{" "}
          (R2 storage and its CDN) and encoded on servers we run.
        </li>
        <li>
          <strong>Google Cloud</strong> encodes some of those clips when our own
          servers are busy. The video is processed on servers we rent in the EU
          just for that encode, and nothing is kept there afterwards.
        </li>
        <li>
          <strong>Discord and Twitch</strong> handle sign-in, Twitch clip import
          and the Discord Activity.
        </li>
        <li>
          <strong>Stripe</strong>, through its Link service, sells paid plans as
          the merchant of record: it takes payment and handles tax, receipts and
          billing support, under its own privacy policy. When you check out we
          send it your Reelshelf account ID and, the first time, your email
          address.
        </li>
        <li>
          <strong>Resend</strong> delivers account emails, such as a new sign-in
          being linked or your library being nearly full.
        </li>
        <li>
          <strong>OpenAI, Anthropic and OpenRouter</strong> (and the providers
          behind it) for the analysis above.
        </li>
        <li>
          <strong>PostHog</strong>, in the EU, decides which accounts see
          features we're still rolling out. If we include you in an early
          release, PostHog holds your account's ID; it isn't sent your name,
          email or anything you do in Reelshelf.
        </li>
        <li>
          <strong>Our hosting provider</strong> runs the servers and database.
        </li>
      </ul>
      <p>
        We share data only to run Reelshelf, or when the law requires us to,
        such as reporting child sexual abuse material to the authorities.
      </p>

      <h2>Where your data is stored</h2>
      <p>
        Reelshelf is run from the UK. Our servers and database are in Germany.
        Clips in our own pipeline are stored by Cloudflare in the US (eastern
        North America); clips not yet moved from Bunny.net are stored in Germany
        until they move. Clips that Google Cloud encodes are processed in the
        EU. Cloudflare, Discord, Twitch, Stripe, OpenAI, Anthropic, OpenRouter
        and Resend are based in the US, so data they handle leaves the UK and
        EU. We rely on their standard data protection terms, such as standard
        contractual clauses or the UK–US data bridge, to protect it.
      </p>

      <h2>Sharing and visibility</h2>
      <p>
        Your clips are visible only to you, to collaborators you add, to anyone
        with a share link to the clip or a shared collection, and to the people
        in a Discord watch room when you play a clip there. Watch rooms aren't
        saved; they end when everyone leaves. Clip video addresses aren't
        secret-signed, so treat clips as unlisted rather than private. A share
        link keeps working until you delete the clip, or until we take the clip
        down after a report.
      </p>

      <h2>How long we keep it</h2>
      <p>
        We keep your data while your account exists. For clips in our own
        pipeline, the file you uploaded is deleted once it has been encoded, and
        only the encoded video is kept. When an existing clip moves from
        Bunny.net to our own pipeline, its copy at Bunny.net is kept for up to 7
        days, so we can switch back if the move went wrong, and is then deleted.
        Deleting a clip removes the video, and any copy still at Bunny.net, from
        storage and deletes its tags, detections and transcripts. Unlinking a
        sign-in method deletes its stored tokens. You can delete your whole
        account in Settings: it stops working straight away, and its clips are
        removed from storage and the rest of its data deleted shortly after,
        usually within minutes. Any paid plan is cancelled first, though Stripe
        keeps its records of past payments for as long as the law requires.
        Collections you made go with it, including for their collaborators. If
        your account has more clips than its plan keeps, for example after a
        paid plan ends, the clips over the limit are deleted 14 days later, and
        a download of them is kept for 28 days after that, then deleted.
        Downloads you ask for are kept for 28 days too. Reports of shared clips,
        and what we decided, are kept as a record of how we dealt with them,
        even after the clip is deleted; deleting your account removes your name
        from reports about your clips. If we remove a clip for child sexual
        abuse or terrorism, we keep a private copy of it, its details and the
        uploading account's details (usernames, sign-in IDs and email) for a
        year, even if the account is deleted, so we can give them to the
        authorities, and then delete them. Nobody can see that copy except us
        and the authorities we give it to. Backups and logs age out on their own
        schedule.
      </p>

      <h2>Your rights</h2>
      <p>
        Under UK and EU data protection law (UK GDPR and GDPR), you can ask for
        a copy of your data, a correction, deletion, or for us to stop or limit
        processing. We process your data to provide the service you signed up
        for and, for security logs and automated clip analysis, for our
        legitimate interests in running and improving Reelshelf. You can
        complain to the UK Information Commissioner's Office (ico.org.uk) or
        your local data protection authority.
      </p>

      <h2>Children</h2>
      <p>
        Reelshelf isn't meant for children under 13, or under the minimum age
        for Discord or Twitch in your country.
      </p>

      <h2>Changes and contact</h2>
      <p>
        We'll change the date at the top when this policy changes, and email you
        about significant changes. For questions or requests, email{" "}
        <LegalContact />.
      </p>
    </>
  );
}
