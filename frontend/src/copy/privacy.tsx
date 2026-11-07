import { LegalContact } from "@/components/Reelshelf/LegalPage";
import { legalOperator } from "./legal";

/**
 * The privacy policy, kept whole so it reads as one document. When it changes, change `updated` too
 * (and see the note on the privacy policy in AGENTS.md).
 */
export const privacyCopy = {
  title: "Privacy policy",
  updated: "7 November 2026",
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
          <strong>Your account:</strong> what your sign-in provider (Discord,
          Twitch or Google) shares with us, such as your username, display name,
          avatar and email address, plus the email address you choose for
          account mail.
        </li>
        <li>
          <strong>What you add:</strong> your clips and the details you give
          them, your collections, collaborators and share links, and any Twitch
          clips you ask us to import.
        </li>
        <li>
          <strong>Your plan and access:</strong> your plan and, if you pay, your
          subscription's status. Payment details go to Stripe and never reach
          us.
        </li>
        <li>
          <strong>How you use Reelshelf:</strong> what we need to run it, such
          as which clips you've watched, the devices you sign in with our apps,
          and the limits on your plan.
        </li>
        <li>
          <strong>Reports:</strong> what you tell us if you report a clip.
        </li>
        <li>
          <strong>Logs:</strong> standard request logs, such as IP address and
          browser, for security and debugging.
        </li>
      </ul>

      <h2>How we use it</h2>
      <p>
        We use your data to provide Reelshelf: to store, convert and show your
        clips, run your account and plan, send account mail, release new
        features to some accounts before others, and keep the service secure.
        Some clips are analysed by AI services, for example to recognise the
        character you play or to make speech searchable. Email us if you'd like
        your clips left out of transcription.
      </p>

      <h2>Cookies and local storage</h2>
      <p>
        We use one cookie to keep you signed in. Your browser's local storage
        keeps settings and progress on your device, such as your theme, player
        volume and uploads in progress. Nothing is used for advertising or
        tracking.
      </p>

      <h2>Who else handles your data</h2>
      <p>
        We use service providers to run Reelshelf: hosting, storage and video
        processing; sign-in (Discord, Twitch and Google); payments (Stripe, as
        the merchant of record, under its own privacy policy); email delivery;
        AI analysis of clips; and releasing new features gradually. They handle
        your data only to provide their service to us. We share data otherwise
        only when the law requires it, such as reporting child sexual abuse
        material to the authorities.
      </p>
      <p>
        Some of these providers are in the US, so your data may leave the UK and
        EU. Where it does, we rely on safeguards such as standard contractual
        clauses or the UK–US data bridge.
      </p>

      <h2>Sharing and visibility</h2>
      <p>
        Your clips are visible to you, collaborators you add, anyone with a
        share link, and people in a Discord watch room when you play a clip
        there. Treat clips as unlisted rather than private: anyone given a
        clip's address can watch it.
      </p>

      <h2>How long we keep it</h2>
      <p>
        We keep your data while your account exists. A deleted clip stays in
        your bin for 30 days, then it and its details are deleted. A Discord
        watch room is deleted 30 seconds after the last person leaves. If you
        delete your account, it stops working straight away and its data is
        deleted shortly after, unless a report about one of its clips is still
        open. If your account holds more clips than its plan keeps, we'll tell
        you before removing the extra ones.
      </p>
      <p>
        Some records are kept longer where we need them: Stripe keeps payment
        records as the law requires; we keep reports and what we decided; and if
        we remove a clip for child sexual abuse or terrorism, we keep it and the
        uploader's details for up to a year for the authorities. Backups and
        logs age out on their own schedule.
      </p>

      <h2>Your rights</h2>
      <p>
        Under UK and EU data protection law, you can ask for a copy of your
        data, a correction or deletion, or for us to stop or limit processing.
        We process your data to provide the service you signed up for and, for
        security, automated clip analysis and releasing features gradually, for
        our legitimate interests in running and improving Reelshelf. You can
        complain to the UK Information Commissioner's Office (ico.org.uk) or
        your local data protection authority.
      </p>

      <h2>Children</h2>
      <p>
        Reelshelf isn't meant for children under 13, or under the minimum age
        for Discord, Twitch or Google in your country.
      </p>

      <h2>Changes and contact</h2>
      <p>
        We'll change the date at the top when this policy changes, and tell you
        in the app or by email about significant changes. For questions or
        requests, including which providers we use, email <LegalContact />.
      </p>
    </>
  );
}
