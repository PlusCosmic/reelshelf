import { useState } from "react";
import { Image, Modal, Box, Title, CloseButton } from "@mantine/core";
import { Carousel } from "@mantine/carousel";

interface ScreenshotGalleryProps {
  screenshots: string[];
}

export function ScreenshotGallery({ screenshots }: ScreenshotGalleryProps) {
  const [opened, setOpened] = useState<string | null>(null);

  return (
    <Box>
      <Title order={5} mb="sm">
        Screenshots
      </Title>
      <Carousel withIndicators>
        {screenshots.map((src, i) => (
          <Carousel.Slide key={i}>
            <Image
              src={src}
              radius="sm"
              fit="contain"
              bg="var(--mantine-color-dark-6)"
              style={{ cursor: "pointer" }}
              onClick={() => setOpened(src)}
            />
          </Carousel.Slide>
        ))}
      </Carousel>

      <Modal
        opened={opened !== null}
        onClose={() => setOpened(null)}
        size="90vw"
        padding={0}
        withCloseButton={false}
        centered
        overlayProps={{ backgroundOpacity: 0.8, blur: 3 }}
        styles={{
          content: { background: "transparent", boxShadow: "none" },
          body: { padding: 0 },
        }}
      >
        <CloseButton
          onClick={() => setOpened(null)}
          size="lg"
          variant="filled"
          style={{ position: "fixed", top: "1rem", right: "1rem", zIndex: 1000 }}
        />
        {opened && (
          <Image
            src={opened}
            fit="contain"
            style={{ maxHeight: "90vh" }}
          />
        )}
      </Modal>
    </Box>
  );
}
