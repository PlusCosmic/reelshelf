import { useState } from "react";
import {
  Button,
  Card,
  Center,
  CloseButton,
  Dialog,
  Group,
  Image,
  Text,
  TextInput,
  UnstyledButton,
} from "@mantine/core";
import classes from "./FrequentLinks.module.scss";
import { IconLinkPlus } from "@tabler/icons-react";
import { useDisclosure } from "@mantine/hooks";
import { useCurrentUser, useLinks, useAddLink, useDeleteLink } from "../hooks/queries";

export default function FrequentLinks() {
  const { data: user, isLoading: loadingUser } = useCurrentUser();
  const { data: links = [], isLoading: loadingLinks } = useLinks();
  const addLinkMutation = useAddLink();
  const deleteLinkMutation = useDeleteLink();
  const [opened, { toggle, close }] = useDisclosure(false);
  const [value, setValue] = useState("");

  function handleDelete(id: string | undefined) {
    if (!id) return;
    deleteLinkMutation.mutate(id);
  }

  async function handleSubmit() {
    try {
      await addLinkMutation.mutateAsync({ url: value });
      close();
      setValue("");
    } catch (e) {
      console.error("Failed to add link", e);
    }
  }

  function handleClick(url: string | undefined) {
    window.open(url, "_self");
  }

  const isLoading = loadingUser || (user && loadingLinks);

  const items = links
    .filter((x) => x.id && x.title && x.url && x.thumbnailUrl)
    .map((item) => (
      <Card className={classes.item} miw="90" maw="90">
        <CloseButton
          size="sm"
          className={classes.deleteButton}
          variant="transparent"
          onClick={() => handleDelete(item.id)}
        />
        <UnstyledButton key={item.title} onClick={() => handleClick(item.url)}>
          <Center>
            <Image
              src={item.thumbnailUrl}
              w={40}
              h={40}
              radius="md"
              fallbackSrc="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' stroke='currentColor' fill='none' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpath d='M10 14a3.5 3.5 0 0 0 5 0l4 -4a3.5 3.5 0 0 0 -5 -5l-.5 .5'%3E%3C/path%3E%3Cpath d='M14 10a3.5 3.5 0 0 0 -5 0l-4 4a3.5 3.5 0 0 0 5 5l.5 -.5'%3E%3C/path%3E%3C/svg%3E"
            />
          </Center>

          <Text maw={70} truncate="end" size="xs" mt={7}>
            {item.title}
          </Text>
        </UnstyledButton>
      </Card>
    ));

  items.push(
    <UnstyledButton
      key="add-link"
      className={classes.item}
      onClick={toggle}
      miw="90"
    >
      <IconLinkPlus size={24} />
    </UnstyledButton>,
  );

  return (
    <div>
      {isLoading && <Text>Loading links..</Text>}
      {!isLoading && links.length > 0 && (
        <div>
          <Group mt="md">
            {items.map((item) => (
              <div>{item}</div>
            ))}
          </Group>
          <Dialog
            opened={opened}
            withCloseButton
            onClose={close}
            size="lg"
            radius="md"
          >
            <Text size="sm" mb="xs" fw={500}>
              Add a link to frequent list
            </Text>

            <Group align="flex-end">
              <TextInput
                value={value}
                onChange={(event) => setValue(event.currentTarget.value)}
                placeholder="https://pluscosmic.dev"
                style={{ flex: 1 }}
              />
              <Button onClick={handleSubmit}>Add</Button>
            </Group>
          </Dialog>
        </div>
      )}
    </div>
  );
}
