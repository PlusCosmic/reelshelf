export function ApexIcon(props: React.ComponentPropsWithoutRef<"svg">) {
  return (
    <svg
      width="800px"
      height="800px"
      viewBox="0 0 48 48"
      xmlns="http://www.w3.org/2000/svg"
      style={{ width: 40, height: 40, color: "white", fill: "white" }}
      {...props}
    >
      <polygon
        className="a"
        fill="white"
        color={"white"}
        points="24 20.039 32.714 35.303 26.75 35.303 37.08 42.391 42.5 38.013 24 5.609 5.5 38.013 10.92 42.391 21.25 35.303 15.286 35.303 24 20.039"
      />
    </svg>
  );
}