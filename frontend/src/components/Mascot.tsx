type Mood = 'hello' | 'happy' | 'think'

// The owl artwork is a single traced SVG served from /public, so the browser caches it once.
const OWL = '/coruja.svg'
const RATIO = 1540 / 1308

/**
 * The AccioBank owl. One illustration; `mood` only changes its idle motion
 * (hello = floats, happy = hops, think = tilts). `head` crops to the face for logos.
 */
export function Mascot({ mood = 'hello', size = 160, head = false, className = '' }: { mood?: Mood; size?: number; head?: boolean; className?: string }) {
  if (head) {
    return (
      <span className={`mascot-head ${className}`} style={{ width: size, height: size }} aria-hidden>
        <img src={OWL} alt="" width={size * 1.8} height={size * 1.8 * RATIO} />
      </span>
    )
  }
  return <img className={`mascot mascot-${mood} ${className}`} src={OWL} alt="" width={size} height={size * RATIO} aria-hidden />
}

/** AccioBank wordmark with the owl's face. */
export function Logo({ size = 34, subtitle }: { size?: number; subtitle?: string }) {
  return (
    <span className="logo-mark">
      <span className="logo-badge" style={{ width: size + 8, height: size + 8 }}>
        <Mascot head size={size} />
      </span>
      <span>
        <span className="logo-word">
          Accio<b>Bank</b>
        </span>
        {subtitle && <small className="muted">{subtitle}</small>}
      </span>
    </span>
  )
}
